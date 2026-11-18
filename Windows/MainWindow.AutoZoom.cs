using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DogSC.Windows.Models;

namespace DogSC.Windows;

public partial class MainWindow
{
    private string? _lastRecordingPath;

    private async void EditRecording_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRecordingPath is not { } path || _recordingService.IsRecording)
            return;
        EditRecordingButton.IsEnabled = false;
        try
        {
            NavigateTo(1, "edit");
            await ImportClipsAsync([path], selectNewClip: true);
        }
        finally
        {
            EditRecordingButton.IsEnabled = true;
        }
    }

    private async Task<ZoomSegment[]> ReadAutoZoomAsync(string path, double duration)
    {
        var trackPath = Path.ChangeExtension(path, ".pointer.json");
        if (!File.Exists(trackPath))
            return [];
        try
        {
            return AutoZoom.Plan(await ReadPointerTrackAsync(trackPath), duration);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, "已导入视频，无法读取自动运镜轨迹。\n" + exception.Message,
                "自动运镜", MessageBoxButton.OK, MessageBoxImage.Warning);
            return [];
        }
    }

    private static async Task<PointerSample[]> ReadPointerTrackAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PointerSample[]>(stream) ?? [];
    }

    private async Task<ZoomSegment[]> RegenerateAutoZoomAsync(ClipItem clip, IReadOnlyList<ZoomSegment> existing)
    {
        var trackPath = Path.ChangeExtension(clip.FilePath, ".pointer.json");
        if (!File.Exists(trackPath))
            throw new FileNotFoundException("找不到这段录屏的鼠标轨迹，无法重新生成自动运镜。");
        var samples = await ReadPointerTrackAsync(trackPath);
        return AutoZoomRegeneration.Replace(samples, clip.StartSeconds, clip.EndSeconds, existing);
    }

    private void CommitAutoZoomEdit(ClipItem clip, ZoomSegment[] segments, bool enabled)
    {
        SaveUndoState();
        ApplyTimelineZooms(clip, segments, enabled);
    }

    private void PreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e) => FitPreviewFrame();

    private void FitPreviewFrame()
    {
        if (_previewElement is not { NaturalVideoWidth: > 0, NaturalVideoHeight: > 0 } media)
            return;

        // Fit and clip in the same centered frame so MediaElement's layout cannot shift the crop.
        var scale = Math.Min(PreviewViewport.ActualWidth / media.NaturalVideoWidth,
            PreviewViewport.ActualHeight / media.NaturalVideoHeight);
        PreviewHost.Width = scale * media.NaturalVideoWidth;
        PreviewHost.Height = scale * media.NaturalVideoHeight;
        UpdatePreviewZoom();
    }

    private void UpdatePreviewZoom()
    {
        if (_previewElement is { } media)
            ApplyPreviewZoom(media.Position.TotalSeconds);
    }

    private void ApplyPreviewZoom(double sourceSeconds)
    {
        if (_previewElement is not { } media)
            return;

        var viewport = _previewClip is { AutoZoomEnabled: true } clip
            ? AutoZoom.At(clip.AutoZoomSegments, Math.Min(sourceSeconds, Math.BitDecrement(clip.EndSeconds)))
            : new ZoomViewport(_previewClip?.ManualZoomAt(sourceSeconds) ?? 1, 0.5, 0.5);
        if (viewport.Zoom == 1)
        {
            media.RenderTransform = Transform.Identity;
            return;
        }

        var offsetX = PreviewHost.Width * (0.5 - viewport.X * viewport.Zoom);
        var offsetY = PreviewHost.Height * (0.5 - viewport.Y * viewport.Zoom);
        media.RenderTransform = new MatrixTransform(viewport.Zoom, 0, 0, viewport.Zoom, offsetX, offsetY);
    }

    private void EditAutoZoom_Click(object sender, RoutedEventArgs e) => EditAutoZoom();

    private void EditAutoZoom(ZoomSegment? selected = null)
    {
        if (ClipSelection.SelectedItem is not ClipItem clip)
            return;

        var rows = new System.Collections.ObjectModel.ObservableCollection<ZoomEditRow>(
            clip.AutoZoomSegments.Select(segment => new ZoomEditRow(segment)));
        var enabled = new CheckBox { Content = "启用运镜轨道", IsChecked = clip.AutoZoomEnabled, Margin = new Thickness(0, 0, 0, 8) };
        var table = new DataGrid
        {
            ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false,
            CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single,
            SelectedItem = rows.FirstOrDefault(row => ReferenceEquals(row.Segment, selected))
        };
        foreach (var (label, property) in new[]
        {
            ("开始（秒）", nameof(ZoomEditRow.Start)), ("结束（秒）", nameof(ZoomEditRow.End)),
            ("倍率", nameof(ZoomEditRow.Zoom)), ("焦点 X", nameof(ZoomEditRow.X)), ("焦点 Y", nameof(ZoomEditRow.Y))
        })
            table.Columns.Add(new DataGridTextColumn { Header = label, Width = new DataGridLength(1, DataGridLengthUnitType.Star), Binding = new Binding(property) });

        var content = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(enabled, Dock.Top);
        content.Children.Add(enabled);
        var hint = new TextBlock
        {
            Text = "时间以源视频为准。焦点坐标为 0～1。修改焦点会将该段设为固定焦点。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(hint, Dock.Top);
        content.Children.Add(hint);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var regenerate = new Button { Content = "重新生成自动运镜", Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "更新当前片段入点到出点之间的自动运镜，可撤销。手动运镜会保留。" };
        var delete = new Button { Content = "删除选中段", Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var apply = new Button { Content = "应用" };
        buttons.Children.Add(regenerate);
        buttons.Children.Add(delete);
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        DockPanel.SetDock(buttons, Dock.Bottom);
        content.Children.Add(buttons);
        content.Children.Add(table);
        var dialog = new Window
        {
            Owner = this, Title = "自动运镜", Width = 620, Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content
        };

        ZoomSegment[] changes = [];
        var regenerated = false;
        delete.Click += (_, _) =>
        {
            if (table.SelectedItem is ZoomEditRow row)
            {
                table.CancelEdit(DataGridEditingUnit.Row);
                rows.Remove(row);
            }
        };
        ZoomSegment[]? ReadEdits()
        {
            if (!table.CommitEdit(DataGridEditingUnit.Cell, true) || !table.CommitEdit(DataGridEditingUnit.Row, true))
                return null;
            var edited = rows.Select(row => row.Parse()).ToArray();
            var ordered = edited.OfType<ZoomSegment>().OrderBy(segment => segment.Start).ToArray();
            if (edited.Length != ordered.Length || !AutoZoom.IsValid(ordered))
            {
                MessageBox.Show(dialog, "请检查数字、起止时间和坐标。镜头区间不能重叠，倍率应在 1～2 之间。", "自动运镜");
                return null;
            }
            return ordered;
        }

        apply.Click += (_, _) =>
        {
            if (ReadEdits() is { } edited)
            {
                changes = edited;
                dialog.DialogResult = true;
            }
        };
        regenerate.Click += async (_, _) =>
        {
            if (ReadEdits() is not { } edited)
                return;
            content.IsEnabled = false;
            try
            {
                var replacement = await RegenerateAutoZoomAsync(clip, edited);
                if (!dialog.IsVisible)
                    return;
                changes = replacement;
                regenerated = true;
                enabled.IsChecked = true;
                dialog.DialogResult = true;
            }
            catch (FileNotFoundException)
            {
                if (dialog.IsVisible)
                    MessageBox.Show(dialog, "找不到这段录屏的鼠标轨迹，无法重新生成自动运镜。", "重新生成自动运镜");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                if (dialog.IsVisible)
                    MessageBox.Show(dialog, "无法读取这段录屏的鼠标轨迹。\n" + exception.Message, "重新生成自动运镜");
            }
            finally
            {
                content.IsEnabled = true;
            }
        };

        if (dialog.ShowDialog() != true)
            return;
        CommitAutoZoomEdit(clip, changes, enabled.IsChecked == true);
        if (regenerated)
        {
            var automaticCount = changes.Count(segment => !segment.IsManual &&
                segment.Start < clip.EndSeconds && segment.End > clip.StartSeconds);
            EditorStatusText.Text = automaticCount > 0
                ? $"已重新生成 {automaticCount} 段自动运镜，可使用撤销恢复。"
                : "当前范围没有达到放大条件的连续操作，已更新自动运镜，可撤销。";
        }
    }

    private sealed class ZoomEditRow(ZoomSegment segment)
    {
        public ZoomSegment Segment => segment;
        public string Start { get; set; } = Format(segment.Start);
        public string End { get; set; } = Format(segment.End);
        public string Zoom { get; set; } = Format(segment.Zoom);
        public string X { get; set; } = Format(segment.X);
        public string Y { get; set; } = Format(segment.Y);

        public ZoomSegment? Parse()
        {
            if (!TryReadDouble(Start, out var start) || !TryReadDouble(End, out var end) ||
                !TryReadDouble(Zoom, out var zoom) || !TryReadDouble(X, out var x) || !TryReadDouble(Y, out var y))
                return null;
            var focus = X == Format(segment.X) && Y == Format(segment.Y) ? segment.Focus : null;
            var keepStart = Start == Format(segment.Start);
            var keepEnd = End == Format(segment.End);
            return segment with
            {
                Start = keepStart ? segment.Start : start,
                End = keepEnd ? segment.End : end,
                CurveStart = keepStart && keepEnd ? segment.CurveStart : null,
                CurveEnd = keepStart && keepEnd ? segment.CurveEnd : null,
                Zoom = zoom,
                X = x,
                Y = y,
                Focus = focus
            };
        }

        private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
