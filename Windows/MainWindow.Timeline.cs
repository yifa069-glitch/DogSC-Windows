using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DogSC.Windows.Models;

namespace DogSC.Windows;

public partial class MainWindow
{
    private TimelineLayout _timeline = new([]);
    private readonly HashSet<ClipItem> _observedTimelineClips = [];
    private readonly Dictionary<string, IReadOnlyList<RecordingMarker>> _timelineMarkers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _timelineSourceDurations = new(StringComparer.OrdinalIgnoreCase);
    private bool _timelineUpdatePending;
    private bool _previewMediaReady;
    private bool _playAfterMediaOpen;
    private readonly DispatcherTimer _previewWarmupTimer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private long _previewWarmupDeadline;
    private readonly DispatcherTimer _timelineHoverTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private double _timelineHoverCandidate;
    private (ClipItem Clip, double SourceTime)? _timelineHoverReturn;
    private const double TimelineEndPadding = 10;

    private double CurrentPreviewSourceTime => _previewMediaReady && _previewPlaying && _previewElement is { } media
        ? media.Position.TotalSeconds : _previewStartSeconds;

    private void InitializeTimeline()
    {
        TimelineView.Markers = _timelineMarkers;
        TimelineView.SourceDurations = _timelineSourceDurations;
        TimelineView.SeekRequested += time => SeekTimeline(time);
        TimelineView.ClipSelected += clip => ClipSelection.SelectedItem = clip;
        TimelineView.ReorderRequested += ReorderTimelineClip;
        TimelineView.TrimRequested += TrimTimelineClip;
        TimelineView.ScrollRequested += delta => TimelineScroll.Value += delta;
        TimelineView.ZoomEditRequested += (clip, segment) =>
        {
            ClipSelection.SelectedItem = clip;
            if (segment is not null)
                EditAutoZoom(segment);
            else
                InspectorTabs.SelectedIndex = 0;
        };
        TimelineView.ZoomRangeRequested += MoveTimelineZoom;
        TimelineView.ZoomCreateRequested += CreateTimelineZoom;
        TimelineView.PreviewHoverRequested += QueueTimelineHover;
        _previewWarmupTimer.Tick += FinishPreviewWarmup;
        _timelineHoverTimer.Tick += (_, _) =>
        {
            _timelineHoverTimer.Stop();
            if (_previewClip is not { } clip || _timeline.Locate(_timelineHoverCandidate) is not { } position)
                return;
            _timelineHoverReturn ??= (clip, CurrentPreviewSourceTime);
            LoadClipPreview(position.Entry.Clip, position.SourceTime);
        };
        _clips.CollectionChanged += (_, _) => QueueTimelineUpdate();
        TimelineOptions_Changed(this, new RoutedEventArgs());
        UpdateTimelineScale();
    }

    private void QueueTimelineUpdate()
    {
        if (_timelineUpdatePending)
            return;
        _timelineUpdatePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RebuildTimeline));
    }

    private void TimelineClip_Changed(object? sender, PropertyChangedEventArgs e)
    {
        QueueTimelineUpdate();
    }

    private void RebuildTimeline()
    {
        _timelineUpdatePending = false;
        var current = new HashSet<ClipItem>(_clips);
        foreach (var clip in _observedTimelineClips.Except(current))
            clip.PropertyChanged -= TimelineClip_Changed;
        foreach (var clip in current.Except(_observedTimelineClips))
        {
            clip.PropertyChanged += TimelineClip_Changed;
            if (!_timelineMarkers.ContainsKey(clip.FilePath))
                _timelineMarkers[clip.FilePath] = ReadTimelineMarkers(clip.FilePath);
        }
        _observedTimelineClips.Clear();
        _observedTimelineClips.UnionWith(current);
        _timeline = new TimelineLayout(_clips);
        TimelineView.Timeline = _timeline;
        UpdateTimelinePosition();
        UpdateTimelineScale();
    }

    private static IReadOnlyList<RecordingMarker> ReadTimelineMarkers(string filePath)
    {
        try
        {
            var path = Path.ChangeExtension(filePath, ".markers.json");
            if (File.Exists(path))
                return JsonSerializer.Deserialize<RecordingMarker[]>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception exception)
        {
            App.WriteDiagnostic("Timeline marker read failed: " + exception.Message);
        }
        return [];
    }

    private void UpdateTimelinePosition(bool follow = false)
    {
        var clip = _timelineHoverReturn?.Clip ?? _previewClip;
        var entry = clip is null ? null : _timeline.Find(clip);
        var sourceTime = _timelineHoverReturn?.SourceTime ?? CurrentPreviewSourceTime;
        var position = entry?.ToProjectTime(sourceTime) ?? 0;
        TimelineView.SelectedClip = ClipSelection.SelectedItem as ClipItem;
        TimelineView.Playhead = position;
        TimelineClock.Text = $"{TimelineTimeLabel(position)} / {TimelineTimeLabel(_timeline.Duration)}";
        if (follow)
        {
            var x = position * TimelineView.PixelsPerSecond;
            if (x < TimelineScroll.Value || x >= TimelineScroll.Value + TimelineView.ActualWidth - TimelineEndPadding)
                TimelineScroll.Value = x - TimelineView.ActualWidth * 0.55;
        }
    }

    private static string TimelineTimeLabel(double time)
    {
        var rounded = Math.Round(time, 2);
        return string.Create(CultureInfo.InvariantCulture, $"{(long)rounded / 60:00}:{rounded % 60:00.00}");
    }

    private void UpdateTimelineScale()
    {
        if (TimelineScroll is null || TimelineView is null || TimelineZoomSlider is null)
            return;
        var width = Math.Max(1, TimelineView.ActualWidth - TimelineEndPadding);
        var previous = TimelineView.PixelsPerSecond;
        var anchor = TimelineView.Playhead * previous - TimelineScroll.Value;
        var basis = width / (_timeline.Duration > 0 ? _timeline.Duration : 24);
        var pixelsPerSecond = Math.Min(360, basis * Math.Pow(2, TimelineZoomSlider.Value));
        // At the fit level, even very short clips use the available width.
        TimelineView.PixelsPerSecond = Math.Max(basis, pixelsPerSecond);
        TimelineScroll.Maximum = Math.Max(0, _timeline.Duration * TimelineView.PixelsPerSecond - width);
        TimelineScroll.ViewportSize = width;
        TimelineScroll.SmallChange = 40;
        TimelineScroll.LargeChange = width * 0.8;
        if (anchor >= 0 && anchor <= width && Math.Abs(previous - TimelineView.PixelsPerSecond) > 0.001)
            TimelineScroll.Value = TimelineView.Playhead * TimelineView.PixelsPerSecond - anchor;
        TimelineView.Offset = TimelineScroll.Value;
    }

    private void TimelineView_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTimelineScale();
    private void TimelineZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateTimelineScale();

    private void TimelineScroll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TimelineView is not null)
            TimelineView.Offset = e.NewValue;
    }

    private void FitTimeline_Click(object sender, RoutedEventArgs e)
    {
        TimelineZoomSlider.Value = 0;
        TimelineScroll.Value = 0;
    }

    private void ZoomInTimeline_Click(object sender, RoutedEventArgs e) => TimelineZoomSlider.Value += 0.5;
    private void ZoomOutTimeline_Click(object sender, RoutedEventArgs e) => TimelineZoomSlider.Value -= 0.5;

    private void PreviousTimelineClip_Click(object sender, RoutedEventArgs e) => SeekAdjacentTimelineClip(-1);
    private void NextTimelineClip_Click(object sender, RoutedEventArgs e) => SeekAdjacentTimelineClip(1);

    private void SeekAdjacentTimelineClip(int direction)
    {
        EndTimelineHover();
        if (_timeline.Clips.Count == 0)
            return;
        var target = Math.Clamp(ClipSelection.SelectedIndex + direction, 0, _timeline.Clips.Count - 1);
        SeekTimeline(_timeline.Clips[target].Start, _previewPlaying || _playAfterMediaOpen);
        UpdateTimelinePosition(follow: true);
    }

    private void TimelineOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (TimelineView is null)
            return;
        TimelineView.ShowWaveform = TimelineWaveform?.IsChecked == true;
        TimelineView.SnapEnabled = TimelineSnap?.IsChecked == true;
    }

    private void TimelineMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void SeekTimeline(double time, bool resume = false)
    {
        EndTimelineHover();
        if (_timeline.Locate(time) is not { } position || _previewTransferPosition.HasValue)
            return;
        ClipSelection.SelectedItem = position.Entry.Clip;
        SeekPreview(position.SourceTime, resume);
    }

    private void QueueTimelineHover(double? time)
    {
        if (TimelineHoverPreview.IsChecked != true || time is null || _previewPlaying ||
            _playAfterMediaOpen || _previewTransferPosition.HasValue)
        {
            EndTimelineHover();
            return;
        }
        _timelineHoverCandidate = time.Value;
        _timelineHoverTimer.Start();
    }

    private void EndTimelineHover()
    {
        _timelineHoverTimer.Stop();
        if (_timelineHoverReturn is not { } previous)
            return;
        _timelineHoverReturn = null;
        if (_clips.Contains(previous.Clip))
            LoadClipPreview(previous.Clip, previous.SourceTime);
    }

    private void TimelineHoverPreview_Unchecked(object sender, RoutedEventArgs e) => EndTimelineHover();

    private void StartPreviewWarmup(MediaElement media)
    {
        // Some Windows decoders ignore paused seeks until playback has advanced once.
        media.IsMuted = true;
        media.Play();
        _previewWarmupDeadline = Environment.TickCount64 + 700;
        _previewWarmupTimer.Start();
    }

    private void FinishPreviewWarmup(object? sender, EventArgs e)
    {
        if (_previewElement is not { Source: not null } media)
        {
            _previewWarmupTimer.Stop();
            return;
        }
        if (media.Position == TimeSpan.Zero && Environment.TickCount64 < _previewWarmupDeadline)
            return;
        _previewWarmupTimer.Stop();
        media.Pause();
        _previewMediaReady = true;
        SeekPreview(_previewStartSeconds, _playAfterMediaOpen);
        media.IsMuted = false;
    }

    private void SeekPreview(double sourceTime, bool resume)
    {
        _previewStartSeconds = sourceTime;
        _playAfterMediaOpen = resume;
        if (_previewMediaReady && _previewElement is { Source: not null } media)
        {
            if (!resume)
                SetPreviewPlayback(false);
            media.Position = TimeSpan.FromSeconds(sourceTime);
            if (resume)
                SetPreviewPlayback(true);
            ApplyPreviewZoom(sourceTime);
        }
        else
        {
            _previewPlaying = false;
            _previewTimer.Stop();
            PreviewPlayButton.Content = resume ? "暂停预览" : "播放预览";
        }
        UpdateTimelinePosition(resume);
    }

    private void SetPreviewPlayback(bool playing)
    {
        if (_previewElement is not { } media)
            return;
        _playAfterMediaOpen = false;
        _previewPlaying = playing;
        if (playing)
        {
            media.Play();
            _previewTimer.Start();
        }
        else
        {
            media.Pause();
            _previewTimer.Stop();
        }
        PreviewPlayButton.Content = playing ? "暂停预览" : "播放预览";
    }

    private void AdvancePreview(ClipItem clip)
    {
        var entry = _timeline.Find(clip);
        if (entry is not null && entry.End < _timeline.Duration)
            SeekTimeline(entry.End, resume: true);
        else
            SeekPreview(clip.EndSeconds, resume: false);
    }

    private void ReorderTimelineClip(ClipItem source, ClipItem target, bool after)
    {
        var from = _clips.IndexOf(source);
        var to = _clips.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
            return;
        to += (after ? 1 : 0) - (from < to ? 1 : 0);
        if (from == to)
            return;
        SaveUndoState();
        _clips.Move(from, to);
        ClipSelection.SelectedItem = source;
        EditorStatusText.Text = "片段顺序已更新。";
    }

    private void MoveTimelineZoom(ClipItem clip, ZoomSegment segment, double start, double end)
    {
        var index = Array.IndexOf(clip.AutoZoomSegments.ToArray(), segment);
        if (!_clips.Contains(clip) || index < 0 || !clip.AutoZoomEnabled)
            return;
        var lower = index == 0 ? 0 : clip.AutoZoomSegments[index - 1].End;
        var upper = index + 1 < clip.AutoZoomSegments.Count ? clip.AutoZoomSegments[index + 1].Start
            : Math.Max(clip.EndSeconds, _timelineSourceDurations.GetValueOrDefault(clip.FilePath));
        var moving = Math.Abs((end - start) - (segment.End - segment.Start)) < 0.001;
        if (moving && upper - lower >= end - start)
        {
            var length = end - start;
            start = Math.Clamp(start, lower, upper - length);
            end = start + length;
        }
        start = Math.Max(start, lower);
        end = Math.Min(end, upper);
        if (end - start < 0.05 ||
            Math.Abs(start - segment.Start) < 0.001 && Math.Abs(end - segment.End) < 0.001)
            return;
        var offset = start - segment.Start;
        var focus = moving ? segment.Focus?.Select(point => point with { Time = point.Time + offset })
            .Where(point => point.Time >= 0).ToArray() : segment.Focus;
        var changed = segment with { Start = start, End = end, Focus = focus, CurveStart = null, CurveEnd = null };
        var ranges = clip.AutoZoomSegments.ToArray();
        ranges[index] = changed;
        if (AutoZoom.IsValid(ranges))
        {
            SaveUndoState();
            ApplyTimelineZooms(clip, ranges, true);
        }
    }

    private void CreateTimelineZoom(ClipItem clip, double start, double end)
    {
        if (!_clips.Contains(clip))
            return;
        start = Math.Max(start, clip.StartSeconds);
        end = Math.Min(end, clip.EndSeconds);
        // Reuse the gap under the drag start, including when the selected clip has disabled zooms.
        foreach (var existing in clip.AutoZoomSegments)
        {
            if (existing.Start <= start && start < existing.End)
                return;
            if (existing.Start > start)
            {
                end = Math.Min(end, existing.Start);
                break;
            }
        }
        if (end - start < 0.05)
            return;
        var segment = new ZoomSegment(start, end, 1.6, 0.5, 0.5) { IsManual = true };
        var ranges = clip.AutoZoomSegments.Append(segment).OrderBy(range => range.Start).ToArray();
        if (!AutoZoom.IsValid(ranges))
            return;
        SaveUndoState();
        ApplyTimelineZooms(clip, ranges, true);
    }

    private void ApplyTimelineZooms(ClipItem clip, ZoomSegment[] ranges, bool enabled)
    {
        clip.AutoZoomEnabled = enabled;
        clip.AutoZoomSegments = ranges;
        QueueTimelineUpdate();
        UpdatePreviewZoom();
    }

    private async void TrimTimelineClip(ClipItem clip, bool trimStart, double sourceTime)
    {
        if (!_clips.Contains(clip))
            return;
        const double minClipLength = 0.1;
        var start = clip.StartSeconds;
        var end = clip.EndSeconds;
        if (trimStart)
            start = Math.Clamp(sourceTime, 0, Math.Max(0, end - minClipLength));
        else
        {
            var sourceEnd = Math.Max(end, _timelineSourceDurations.GetValueOrDefault(clip.FilePath));
            end = Math.Clamp(sourceTime, Math.Min(sourceEnd, start + minClipLength), sourceEnd);
        }
        if (Math.Abs(start - clip.StartSeconds) < 0.001 && Math.Abs(end - clip.EndSeconds) < 0.001)
            return;
        SaveUndoState();
        clip.StartSeconds = start;
        clip.EndSeconds = end;
        clip.FadeInSeconds = Math.Min(clip.FadeInSeconds, end - start);
        clip.FadeOutSeconds = Math.Min(clip.FadeOutSeconds, end - start - clip.FadeInSeconds);
        ClipSelection.SelectedItem = clip;
        ClipStartTextBox.Text = start.ToString("0.###", CultureInfo.InvariantCulture);
        ClipEndTextBox.Text = end.ToString("0.###", CultureInfo.InvariantCulture);
        ClipFadeInTextBox.Text = clip.FadeInSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        ClipFadeOutTextBox.Text = clip.FadeOutSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        SeekPreview(trimStart ? start : end, resume: false);
        EditorStatusText.Text = $"已裁剪至 {start:F2}–{end:F2} 秒。";
        if (trimStart)
        {
            var thumbnail = await CreateClipThumbnailAsync(clip.FilePath, start);
            if (clip.StartSeconds == start && thumbnail is not null)
                clip.ThumbnailPath = thumbnail;
        }
    }
}
