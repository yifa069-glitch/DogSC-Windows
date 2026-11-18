using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DogSC.Windows.Models;

namespace DogSC.Windows.Controls;

public sealed class TimelineTrack : FrameworkElement
{
    public const double GutterWidth = 0;
    private const double ClipTop = 29;
    private const double ClipHeight = 57;
    private const double ZoomTop = 95;
    private const double ZoomHeight = 25;
    private const double TrackHeight = 134;
    private const double SnapDistance = 7;
    private const double ThumbnailWidth = 68;
    private static readonly Brush Background = Paint("#FAFBFD");
    private static readonly Brush TrackBackground = Paint("#F4F6F9");
    private static readonly Brush Divider = Paint("#DDE3EC");
    private static readonly Brush Ink = Paint("#23314A");
    private static readonly Brush Muted = Paint("#6F7A8A");
    private static readonly Brush Blue = Paint("#1769F5");
    private static readonly Brush PlayheadBrush = Paint("#E45441");
    private static readonly Brush MarkerBrush = Paint("#8871B6");
    private static readonly Brush ZoomFill = Paint("#EAF4DC");
    private static readonly Brush ZoomBorder = Paint("#AFCC88");
    private static readonly Brush ZoomText = Paint("#3C591F");
    private static readonly Brush BadgeFill = Paint("#D9293447");
    private static readonly Brush WaveBackground = Paint("#E9EEF5");
    private static readonly Brush TrimShade = Paint("#A6FFFFFF");
    private static readonly Typeface LabelFont = new("Microsoft YaHei UI");
    private readonly Dictionary<string, BitmapImage?> _images = new(StringComparer.OrdinalIgnoreCase);
    private TimelineLayout? _timeline;
    private double _offset;
    private double _pixelsPerSecond = 40;
    private double _playhead;
    private ClipItem? _selectedClip;
    private bool _showWaveform;
    private bool _snapEnabled = true;
    private IReadOnlyDictionary<string, IReadOnlyList<RecordingMarker>> _markers =
        new Dictionary<string, IReadOnlyList<RecordingMarker>>();
    private IReadOnlyDictionary<string, double> _sourceDurations = new Dictionary<string, double>();
    private Point _pressed;
    private double _pressedOffset;
    private TimelineClip? _pressedClip;
    private TimelineZoom? _pressedZoom;
    private TimelineClip? _dropTarget;
    private bool _dropAfter;
    private bool _moved;
    private double _trimPosition;
    private (double Start, double End) _zoomRange;
    private Gesture _gesture;

    public TimelineTrack()
    {
        Focusable = true;
        ClipToBounds = true;
        AutomationProperties.SetName(this, "项目时间线");
        AutomationProperties.SetHelpText(this, "点击或拖动时间刻度定位；拖动素材调整顺序，拖动两端裁剪。左右方向键移动播放头。");
    }

    public TimelineLayout? Timeline
    {
        get => _timeline;
        set
        {
            if (ReferenceEquals(_timeline, value))
                return;
            _timeline = value;
            var livePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (value is not null)
            {
                foreach (var entry in value.Clips)
                {
                    if (entry.Clip.ThumbnailPath is { } thumb)
                        livePaths.Add(thumb);
                    if (entry.Clip.AudioWaveformPath is { } waveform)
                        livePaths.Add(waveform);
                }
            }
            foreach (var path in _images.Keys.Where(path => !livePaths.Contains(path)).ToArray())
                _images.Remove(path);
            InvalidateVisual();
        }
    }

    public double Offset
    {
        get => _offset;
        set => SetValueAndRedraw(ref _offset, Nonnegative(value));
    }

    public double PixelsPerSecond
    {
        get => _pixelsPerSecond;
        set => SetValueAndRedraw(ref _pixelsPerSecond, double.IsFinite(value) ? Math.Max(0.001, value) : 40);
    }

    public double Playhead
    {
        get => _playhead;
        set => SetValueAndRedraw(ref _playhead, Nonnegative(value));
    }

    public ClipItem? SelectedClip
    {
        get => _selectedClip;
        set => SetValueAndRedraw(ref _selectedClip, value);
    }

    public bool ShowWaveform
    {
        get => _showWaveform;
        set => SetValueAndRedraw(ref _showWaveform, value);
    }

    public bool SnapEnabled
    {
        get => _snapEnabled;
        set => _snapEnabled = value;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<RecordingMarker>> Markers
    {
        get => _markers;
        set
        {
            _markers = value;
            InvalidateVisual();
        }
    }

    public IReadOnlyDictionary<string, double> SourceDurations
    {
        get => _sourceDurations;
        set
        {
            _sourceDurations = value;
            InvalidateVisual();
        }
    }

    public event Action<double>? SeekRequested;
    public event Action<ClipItem>? ClipSelected;
    public event Action<ClipItem, ClipItem, bool>? ReorderRequested;
    public event Action<ClipItem, bool, double>? TrimRequested;
    public event Action<ClipItem, ZoomSegment?>? ZoomEditRequested;
    public event Action<ClipItem, ZoomSegment, double, double>? ZoomRangeRequested;
    public event Action<ClipItem, double, double>? ZoomCreateRequested;
    public event Action<double>? ScrollRequested;
    public event Action<double?>? PreviewHoverRequested;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsFinite(availableSize.Width) ? availableSize.Width : 0, TrackHeight);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        if (width <= GutterWidth)
            return;

        dc.DrawRectangle(Background, null, new Rect(0, 0, width, ActualHeight));
        dc.DrawRectangle(TrackBackground, null, new Rect(0, ClipTop - 1, width, ClipHeight + 2));
        foreach (var y in new[] { ClipTop - 1, ClipTop + ClipHeight + 1, TrackHeight - 1 })
            dc.DrawLine(new Pen(Divider, 1), new Point(0, y), new Point(width, y));

        dc.PushClip(new RectangleGeometry(new Rect(GutterWidth, 0, width - GutterWidth, ActualHeight)));
        var timeline = Timeline;
        if (timeline is null || timeline.Duration <= 0)
        {
            DrawLabel(dc, "导入视频后，片段和运镜会显示在这里。", 12, Muted,
                new Point(GutterWidth + 12, ClipTop + 18));
        }
        else
        {
            DrawRuler(dc, timeline.Duration);
            foreach (var entry in timeline.Clips)
            {
                if (X(entry.End) <= GutterWidth || X(entry.Start) >= width || entry.Duration <= 0)
                    continue;
                DrawClip(dc, entry);
                DrawZooms(dc, entry);
                DrawMarkers(dc, entry);
            }
            DrawDragPreview(dc);
            DrawPlayhead(dc);
        }
        dc.Pop();
    }

    private void DrawRuler(DrawingContext dc, double duration)
    {
        var minimumStep = 52 / PixelsPerSecond;
        var scale = Math.Pow(10, Math.Floor(Math.Log10(minimumStep)));
        var magnitude = minimumStep / scale;
        var step = scale * (magnitude <= 1 ? 1 : magnitude <= 2 ? 2 : magnitude <= 5 ? 5 : 10);
        var tickStep = step / 5;
        var first = Math.Max(0, Math.Ceiling(Offset / PixelsPerSecond / tickStep - 1e-8));
        var end = Math.Min(duration, (Offset + ActualWidth - GutterWidth) / PixelsPerSecond);
        var edgePen = new Pen(Divider, 1);
        for (var tick = first; tick * tickStep <= end + 1e-8; tick++)
        {
            var time = tick * tickStep;
            var x = X(time);
            var major = tick % 5 == 0;
            dc.DrawLine(edgePen, new Point(x, major ? 18 : 23), new Point(x, ClipTop - 2));
            if (major)
                DrawLabel(dc, FormatRulerTime(time, step), 10, Muted, new Point(x + 4, 3));
        }
    }

    private void DrawClip(DrawingContext dc, TimelineClip entry)
    {
        var x = X(entry.Start);
        var width = Math.Max(0.5, entry.Duration * PixelsPerSecond - 1.5);
        var selected = ReferenceEquals(SelectedClip, entry.Clip);
        var rect = new Rect(x + 0.75, ClipTop + 2, width, ClipHeight - 4);
        var border = new Pen(selected ? Blue : Divider, selected ? 1.7 : 1);
        dc.DrawRoundedRectangle(WaveBackground, null, rect, 5, 5);
        dc.PushClip(new RectangleGeometry(rect, 5, 5));
        if (ShowWaveform)
        {
            DrawWaveform(dc, entry, new Rect(rect.X + 2, rect.Y + 19, Math.Max(0.5, rect.Width - 4), rect.Height - 22));
            DrawClipName(dc, entry.Clip.FileName, rect);
        }
        else
        {
            var thumb = ReadImage(entry.Clip.ThumbnailPath);
            var imageArea = new Rect(rect.X, rect.Y, rect.Width, rect.Height - 13);
            if (thumb is null)
                DrawClipName(dc, entry.Clip.FileName, rect);
            else
                DrawFilmstrip(dc, thumb, imageArea);
            DrawWaveform(dc, entry, new Rect(rect.X + 1, imageArea.Bottom + 2, Math.Max(0.5, rect.Width - 2), 9));
        }

        var badge = Label($"{entry.Duration:0.##}s", 10, Brushes.White);
        var right = Math.Min(rect.Right, ActualWidth) - 4;
        if (right - Math.Max(GutterWidth, rect.Left) > badge.Width + 14)
        {
            var badgeRect = new Rect(right - badge.Width - 8, rect.Top + 3, badge.Width + 8, badge.Height + 3);
            dc.DrawRoundedRectangle(BadgeFill, null, badgeRect, 5, 5);
            dc.DrawText(badge, new Point(badgeRect.Left + 4, badgeRect.Top + 1));
        }
        dc.Pop();
        dc.DrawRoundedRectangle(null, border, rect, 5, 5);

        if (selected && width >= 24)
        {
            var handle = new Pen(Blue, 2);
            foreach (var edge in new[] { rect.Left + 3, rect.Right - 3 })
                dc.DrawLine(handle, new Point(edge, rect.Top + 20), new Point(edge, rect.Bottom - 16));
        }
    }

    private void DrawClipName(DrawingContext dc, string name, Rect rect)
    {
        var left = Math.Max(rect.Left, GutterWidth) + 6;
        var width = Math.Min(rect.Right, ActualWidth) - left - 4;
        if (width < 6)
            return;
        var text = Label(name, 10, Ink);
        text.MaxTextWidth = width;
        text.MaxLineCount = 1;
        text.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(text, new Point(left, rect.Top + 3));
    }

    private void DrawFilmstrip(DrawingContext dc, BitmapSource thumb, Rect area)
    {
        var left = Math.Max(GutterWidth, area.Left);
        var right = Math.Min(ActualWidth, area.Right);
        var tile = Math.Floor((left - area.Left) / ThumbnailWidth);
        var drawWidth = Math.Max(ThumbnailWidth, area.Height * thumb.PixelWidth / thumb.PixelHeight);
        var drawHeight = Math.Max(area.Height, ThumbnailWidth * thumb.PixelHeight / thumb.PixelWidth);
        for (var x = area.Left + tile * ThumbnailWidth; x < right; x += ThumbnailWidth)
        {
            dc.PushClip(new RectangleGeometry(new Rect(x, area.Y, Math.Min(ThumbnailWidth, area.Right - x), area.Height)));
            dc.DrawImage(thumb, new Rect(x + (ThumbnailWidth - drawWidth) / 2, area.Y + (area.Height - drawHeight) / 2,
                drawWidth, drawHeight));
            dc.Pop();
            dc.DrawLine(new Pen(Brushes.White, 0.45), new Point(x, area.Y), new Point(x, area.Bottom));
        }
    }

    private void DrawWaveform(DrawingContext dc, TimelineClip entry, Rect area)
    {
        var wave = ReadImage(entry.Clip.AudioWaveformPath);
        if (wave is null)
        {
            dc.DrawLine(new Pen(Divider, 1), new Point(area.Left, area.Top + area.Height / 2),
                new Point(area.Right, area.Top + area.Height / 2));
            return;
        }
        var duration = _sourceDurations.GetValueOrDefault(entry.Clip.FilePath, entry.SourceEnd);
        var sourceLength = entry.SourceEnd - entry.SourceStart;
        var scale = area.Width / sourceLength;
        dc.PushClip(new RectangleGeometry(area));
        dc.DrawImage(wave, new Rect(area.Left - entry.SourceStart * scale, area.Top,
            Math.Max(duration, entry.SourceEnd) * scale, area.Height));
        dc.Pop();
    }

    private void DrawZooms(DrawingContext dc, TimelineClip entry)
    {
        foreach (var zoom in entry.GetZooms())
            DrawZoom(dc, zoom.Start, zoom.End,
                $"缩放 {zoom.Segment.Zoom:0.##}× · {(zoom.Segment.IsManual ? "手动" : "自动")}");
        if (HasManualZoom(entry))
        {
            var label = entry.Clip.ZoomStart == entry.Clip.ZoomEnd
                ? $"缩放 {entry.Clip.ZoomStart:0.##}× · 手动"
                : $"缩放 {entry.Clip.ZoomStart:0.##}× → {entry.Clip.ZoomEnd:0.##}×";
            DrawZoom(dc, entry.Start, entry.End, label);
        }
    }

    private void DrawZoom(DrawingContext dc, double start, double end, string label)
    {
        var left = Math.Max(GutterWidth, X(start));
        var right = Math.Min(ActualWidth, X(end));
        if (right <= left)
            return;
        var rect = new Rect(X(start) + 0.5, ZoomTop, Math.Max(0.5, (end - start) * PixelsPerSecond - 1), ZoomHeight);
        dc.DrawRoundedRectangle(ZoomFill, new Pen(ZoomBorder, 1), rect, 5, 5);
        var text = Label(label, 11, ZoomText);
        if (right - left >= text.Width + 10)
            dc.DrawText(text, new Point((left + right - text.Width) / 2, ZoomTop + (ZoomHeight - text.Height) / 2));
    }

    private void DrawMarkers(DrawingContext dc, TimelineClip entry)
    {
        foreach (var marker in MarkersFor(entry))
        {
            var x = X(marker.Time);
            if (x < GutterWidth - 4 || x > ActualWidth + 4)
                continue;
            var diamond = new StreamGeometry();
            using (var shape = diamond.Open())
            {
                shape.BeginFigure(new Point(x, 15), true, true);
                shape.PolyLineTo([new Point(x + 5, 20), new Point(x, 25), new Point(x - 5, 20)], true, false);
            }
            dc.DrawGeometry(MarkerBrush, null, diamond);
        }
    }

    private void DrawPlayhead(DrawingContext dc)
    {
        var x = Math.Clamp(X(Math.Min(Timeline?.Duration ?? 0, Playhead)), GutterWidth, Math.Max(GutterWidth, ActualWidth - 1));
        if (X(Playhead) < GutterWidth - 1 || X(Playhead) > ActualWidth + 1)
            return;
        dc.DrawLine(new Pen(PlayheadBrush, 1.2), new Point(x, 13), new Point(x, TrackHeight - 1));
        dc.DrawRoundedRectangle(PlayheadBrush, null, new Rect(x - 4.5, 1, 9, 14), 3, 3);
    }

    private void DrawDragPreview(DrawingContext dc)
    {
        if (!_moved || _pressedClip is null)
            return;
        if (_pressedZoom is not null || _gesture == Gesture.ZoomCreate)
        {
            var left = X(_pressedClip.ToProjectTime(_zoomRange.Start));
            var right = X(_pressedClip.ToProjectTime(_zoomRange.End));
            dc.DrawRoundedRectangle(ZoomFill, new Pen(Blue, 1.5),
                new Rect(left, ZoomTop, Math.Max(0.5, right - left), ZoomHeight), 5, 5);
        }
        else if (_gesture is Gesture.TrimStart or Gesture.TrimEnd)
        {
            var x = X(UnclampedProjectTime(_pressedClip, _trimPosition));
            var old = X(_gesture == Gesture.TrimStart ? _pressedClip.Start : _pressedClip.End);
            dc.DrawRectangle(TrimShade, null, new Rect(Math.Min(x, old), ClipTop, Math.Abs(x - old), ClipHeight));
            dc.DrawLine(new Pen(Blue, 2), new Point(x, ClipTop), new Point(x, ClipTop + ClipHeight));
        }
        else if (_dropTarget is not null && !ReferenceEquals(_dropTarget, _pressedClip))
        {
            var x = X(_dropAfter ? _dropTarget.End : _dropTarget.Start);
            dc.DrawLine(new Pen(Blue, 3), new Point(x, ClipTop), new Point(x, ClipTop + ClipHeight));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var point = e.GetPosition(this);
        if (point.X < GutterWidth || Timeline is not { Duration: > 0 } timeline)
            return;

        PreviewHoverRequested?.Invoke(null);
        Focus();
        var time = TimeAt(point.X);
        var entry = time <= timeline.Duration ? timeline.Locate(time)?.Entry : null;
        var inZoomLane = point.Y >= ZoomTop && point.Y <= ZoomTop + ZoomHeight;
        var zoom = inZoomLane && entry is not null ? FindZoom(entry, time) : null;
        if (inZoomLane && entry is not null && (zoom.HasValue || HasManualZoom(entry)) && e.ClickCount == 2)
        {
            ClipSelected?.Invoke(entry.Clip);
            SeekRequested?.Invoke(time);
            ZoomEditRequested?.Invoke(entry.Clip, zoom?.Segment);
        }
        else
        {
            _pressed = point;
            _pressedOffset = Offset;
            _pressedClip = null;
            _moved = false;
            if (entry is not null && zoom is { } hit)
            {
                _pressedClip = entry;
                _pressedZoom = hit;
                _gesture = ZoomGesture(hit, point.X);
                _zoomRange = (hit.Segment.Start, hit.Segment.End);
                ClipSelected?.Invoke(entry.Clip);
            }
            else if (inZoomLane && entry is not null && !HasManualZoom(entry) && ZoomCreateRequested is not null)
            {
                _pressedClip = entry;
                _gesture = Gesture.ZoomCreate;
                ClipSelected?.Invoke(entry.Clip);
            }
            else if (point.Y >= ClipTop && point.Y <= ClipTop + ClipHeight &&
                time <= timeline.Duration + SnapDistance / PixelsPerSecond && timeline.Locate(time) is { } position)
            {
                _pressedClip = position.Entry;
                _gesture = ClipGesture(position.Entry, point.X);
                _trimPosition = _gesture == Gesture.TrimStart ? position.Entry.SourceStart : position.Entry.SourceEnd;
                ClipSelected?.Invoke(position.Entry.Clip);
            }
            else
            {
                _gesture = Gesture.Seek;
                SeekAt(point);
            }
            CaptureMouse();
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        if (!IsMouseCaptured || _gesture == Gesture.None)
        {
            ShowPointerFeedback(point);
            var time = TimeAt(point.X);
            PreviewHoverRequested?.Invoke(Timeline is { } timeline && time >= 0 && time <= timeline.Duration ? time : null);
            return;
        }

        if (_gesture == Gesture.Seek)
        {
            SeekAt(point);
            return;
        }
        if (_pressedClip is not { } clip)
            return;
        _moved |= Math.Abs(point.X - _pressed.X) >= SystemParameters.MinimumHorizontalDragDistance;
        if (!_moved)
            return;
        if (_pressedZoom is { } zoom)
        {
            CalculateZoomRange(clip, zoom, point);
        }
        else if (_gesture == Gesture.ZoomCreate)
        {
            double SourceAt(double x) => Math.Clamp(clip.SourceStart + (Snap(TimeAt(x)) - clip.Start) * clip.Speed,
                clip.SourceStart, clip.SourceEnd);
            var start = SourceAt(_pressed.X + _pressedOffset - Offset);
            var end = SourceAt(point.X);
            _zoomRange = (Math.Min(start, end), Math.Max(start, end));
            Cursor = Cursors.Cross;
        }
        else if (_gesture == Gesture.Clip)
        {
            var time = TimeAt(point.X);
            _dropTarget = Timeline?.Locate(time)?.Entry;
            _dropAfter = _dropTarget is not null && time >= (_dropTarget.Start + _dropTarget.End) / 2;
            Cursor = Cursors.SizeAll;
        }
        else
        {
            var start = _gesture == Gesture.TrimStart;
            var original = start ? clip.Start : clip.End;
            var time = original + (point.X - _pressed.X + Offset - _pressedOffset) / PixelsPerSecond;
            var snapped = Snap(time, original, includePlayhead: true);
            var source = clip.SourceStart + (snapped - clip.Start) * clip.Speed;
            var sourceDuration = SourceDurations.GetValueOrDefault(clip.Clip.FilePath, clip.SourceEnd);
            var minimumLength = Math.Min(0.05, clip.SourceEnd - clip.SourceStart);
            _trimPosition = start
                ? Math.Clamp(source, 0, clip.SourceEnd - minimumLength)
                : Math.Clamp(source, clip.SourceStart + minimumLength, Math.Max(sourceDuration, clip.SourceEnd));
            Cursor = Cursors.SizeWE;
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured)
            return;
        var gesture = _gesture;
        var clip = _pressedClip;
        var moved = _moved;
        var target = _dropTarget;
        var after = _dropAfter;
        var sourceTime = _trimPosition;
        var zoom = _pressedZoom;
        var range = _zoomRange;
        ReleaseMouseCapture();

        if (clip is not null)
        {
            if (!moved)
                SeekAt(e.GetPosition(this));
            else if (zoom is { } hit)
                ZoomRangeRequested?.Invoke(clip.Clip, hit.Segment, range.Start, range.End);
            else if (gesture == Gesture.ZoomCreate && range.End - range.Start >= 0.05)
                ZoomCreateRequested?.Invoke(clip.Clip, range.Start, range.End);
            else if (gesture == Gesture.Clip && target is not null && !ReferenceEquals(clip, target))
                ReorderRequested?.Invoke(clip.Clip, target.Clip, after);
            else if (gesture is Gesture.TrimStart or Gesture.TrimEnd)
                TrimRequested?.Invoke(clip.Clip, gesture == Gesture.TrimStart, sourceTime);
        }
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _gesture = Gesture.None;
        _pressedClip = null;
        _pressedZoom = null;
        _dropTarget = null;
        _moved = false;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (ScrollRequested is null)
            return;
        ScrollRequested(-e.Delta);
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        PreviewHoverRequested?.Invoke(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && IsMouseCaptured)
        {
            ReleaseMouseCapture();
            e.Handled = true;
        }
        else if (Timeline is { Duration: > 0 } timeline && e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : 0.1;
            var time = e.Key switch
            {
                Key.Left => Playhead - step,
                Key.Right => Playhead + step,
                Key.Home => 0,
                _ => timeline.Duration
            };
            SeekRequested?.Invoke(Math.Clamp(time, 0, timeline.Duration));
            e.Handled = true;
        }
    }

    private Gesture ClipGesture(TimelineClip entry, double pointerX)
    {
        var width = entry.Duration * PixelsPerSecond;
        if (width < 16)
            return Gesture.Clip;
        var handleWidth = Math.Min(7, width / 4);
        if (Math.Abs(pointerX - X(entry.Start)) <= handleWidth)
            return Gesture.TrimStart;
        if (Math.Abs(pointerX - X(entry.End)) <= handleWidth)
            return Gesture.TrimEnd;
        return Gesture.Clip;
    }

    private Gesture ZoomGesture(TimelineZoom zoom, double pointerX)
    {
        var width = (zoom.End - zoom.Start) * PixelsPerSecond;
        if (width >= 16)
        {
            var handle = Math.Min(7, width / 4);
            if (Math.Abs(pointerX - X(zoom.Start)) <= handle)
                return Gesture.ZoomStart;
            if (Math.Abs(pointerX - X(zoom.End)) <= handle)
                return Gesture.ZoomEnd;
        }
        return Gesture.ZoomMove;
    }

    private void CalculateZoomRange(TimelineClip clip, TimelineZoom zoom, Point point)
    {
        var start = zoom.Segment.Start;
        var end = zoom.Segment.End;
        var boundStart = Math.Min(clip.SourceStart, start);
        var boundEnd = Math.Max(clip.SourceEnd, end);
        var delta = (point.X - _pressed.X + Offset - _pressedOffset) / PixelsPerSecond;

        double SnapDelta(double source)
        {
            var baseline = UnclampedProjectTime(clip, source);
            return (Snap(baseline + delta, baseline, includePlayhead: true, excludedZoom: zoom.Segment) - baseline) * clip.Speed;
        }

        var minimumLength = Math.Min(0.05, end - start);
        switch (_gesture)
        {
            case Gesture.ZoomStart:
                _zoomRange = (Math.Clamp(start + SnapDelta(start), boundStart, end - minimumLength), end);
                break;
            case Gesture.ZoomEnd:
                _zoomRange = (start, Math.Clamp(end + SnapDelta(end), start + minimumLength, boundEnd));
                break;
            default:
                var startDelta = SnapDelta(start);
                var endDelta = SnapDelta(end);
                var plainDelta = delta * clip.Speed;
                var best = Math.Abs(startDelta - plainDelta) >= Math.Abs(endDelta - plainDelta) ? startDelta : endDelta;
                var change = Math.Clamp(best, boundStart - start, boundEnd - end);
                _zoomRange = (start + change, end + change);
                break;
        }
        Cursor = _gesture == Gesture.ZoomMove ? Cursors.SizeAll : Cursors.SizeWE;
    }

    private void ShowPointerFeedback(Point point)
    {
        var time = TimeAt(point.X);
        var entry = Timeline is { } timeline && time >= 0 && time <= timeline.Duration && point.X >= GutterWidth
            ? timeline.Locate(time)?.Entry : null;
        string? hint = null;
        Cursor = Cursors.Arrow;
        if (entry is not null && point.Y < ClipTop)
        {
            Cursor = Cursors.Cross;
            var marker = MarkersFor(entry).FirstOrDefault(marker => Math.Abs(X(marker.Time) - point.X) <= 5);
            hint = marker.Marker?.DisplayLabel;
        }
        else if (entry is not null && point.Y <= ClipTop + ClipHeight)
        {
            var gesture = ClipGesture(entry, point.X);
            Cursor = gesture == Gesture.Clip ? Cursors.Hand : Cursors.SizeWE;
            hint = gesture switch
            {
                Gesture.TrimStart => "拖动调整入点",
                Gesture.TrimEnd => "拖动调整出点",
                _ => entry.Clip.DisplayLabel
            };
        }
        else if (entry is not null && point.Y >= ZoomTop && point.Y <= ZoomTop + ZoomHeight &&
            (FindZoom(entry, time).HasValue || HasManualZoom(entry)))
        {
            var zoom = FindZoom(entry, time);
            Cursor = zoom is { } auto && ZoomGesture(auto, point.X) != Gesture.ZoomMove ? Cursors.SizeWE : Cursors.Hand;
            hint = HasManualZoom(entry) ? "双击打开片段缩放设置" : "拖动移动或裁剪运镜，双击编辑运镜";
        }
        else if (entry is not null && point.Y >= ZoomTop && point.Y <= ZoomTop + ZoomHeight && ZoomCreateRequested is not null)
        {
            Cursor = Cursors.Cross;
            hint = "拖动创建缩放运镜";
        }
        if (!Equals(ToolTip, hint))
            ToolTip = hint;
    }

    private void SeekAt(Point point)
    {
        if (Timeline is not { } timeline)
            return;
        var time = Math.Clamp(TimeAt(point.X), 0, timeline.Duration);
        SeekRequested?.Invoke(Snap(time));
    }

    private double Snap(double time, double? excludedBoundary = null, bool includePlayhead = false, ZoomSegment? excludedZoom = null)
    {
        if (!SnapEnabled || Timeline is not { } timeline || timeline.Locate(time) is not { } position)
            return time;

        var best = time;
        var distance = SnapDistance / PixelsPerSecond;
        void Consider(double candidate)
        {
            if (excludedBoundary is double excluded && Math.Abs(candidate - excluded) < 0.00001)
                return;
            var difference = Math.Abs(candidate - time);
            if (difference < distance)
            {
                distance = difference;
                best = candidate;
            }
        }

        if (includePlayhead)
            Consider(Playhead);
        var index = position.Entry.Index;
        for (var candidateIndex = Math.Max(0, index - 1); candidateIndex <= Math.Min(timeline.Clips.Count - 1, index + 1); candidateIndex++)
        {
            var entry = timeline.Clips[candidateIndex];
            Consider(entry.Start);
            Consider(entry.End);
            foreach (var zoom in entry.GetZooms())
            {
                if (!ReferenceEquals(zoom.Segment, excludedZoom))
                {
                    Consider(zoom.Start);
                    Consider(zoom.End);
                }
            }
            foreach (var marker in MarkersFor(entry))
                Consider(marker.Time);
        }
        return best;
    }

    private IEnumerable<TimelineMarker> MarkersFor(TimelineClip entry) =>
        Markers.TryGetValue(entry.Clip.FilePath, out var markers) ? entry.GetMarkers(markers) : [];

    private static TimelineZoom? FindZoom(TimelineClip entry, double time)
    {
        foreach (var zoom in entry.GetZooms())
        {
            if (time >= zoom.Start && time <= zoom.End)
                return zoom;
        }
        return null;
    }

    private static bool HasManualZoom(TimelineClip entry) =>
        !entry.Clip.AutoZoomEnabled && (entry.Clip.ZoomStart != 1 || entry.Clip.ZoomEnd != 1);

    private double TimeAt(double x) => (x - GutterWidth + Offset) / PixelsPerSecond;
    private double X(double time) => GutterWidth + time * PixelsPerSecond - Offset;
    private static double UnclampedProjectTime(TimelineClip entry, double source) => entry.Start + (source - entry.SourceStart) / entry.Speed;
    private static double Nonnegative(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;

    private void SetValueAndRedraw<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        InvalidateVisual();
    }

    private BitmapImage? ReadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (_images.TryGetValue(path, out var cached))
            return cached;
        BitmapImage? bitmap = null;
        try
        {
            if (File.Exists(path))
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(Path.GetFullPath(path));
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
            }
        }
        catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            bitmap = null;
        }
        _images[path] = bitmap;
        return bitmap;
    }

    private FormattedText Label(string text, double size, Brush foreground) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFont, size, foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void DrawLabel(DrawingContext dc, string text, double size, Brush foreground, Point point) =>
        dc.DrawText(Label(text, size, foreground), point);

    private static string FormatRulerTime(double seconds, double step)
    {
        var time = TimeSpan.FromSeconds(seconds);
        var minutes = (long)time.TotalMinutes;
        var label = minutes >= 60
            ? $"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{minutes}:{time.Seconds:00}";
        return step < 1 ? $"{label}.{time.Milliseconds / 100}" : label;
    }

    private static Brush Paint(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private enum Gesture { None, Seek, Clip, TrimStart, TrimEnd, ZoomMove, ZoomStart, ZoomEnd, ZoomCreate }
}
