namespace DogSC.Windows.Models;

public sealed record PointerSample(double Time, double X, double Y, bool Click);
public sealed record ZoomFocus(double Time, double X, double Y);
public sealed record ZoomSegment(double Start, double End, double Zoom, double X, double Y, ZoomFocus[]? Focus = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsManual { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool UsesGentleMotion { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? CurveStart { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? CurveEnd { get; init; }
}
public readonly record struct ZoomViewport(double Zoom, double X, double Y);

public static class AutoZoom
{
    public const double TransitionSeconds = 0.7;
    private const double ClickGap = 2.2;
    private const double MinimumActionSpan = 0.3;
    private const double MaximumRegionSpan = 0.5;
    private const double EaseInSeconds = 0.95;
    private const double EaseOutSeconds = 1.15;
    private const double HoldSeconds = 1.0;
    private const double MinimumOverviewSeconds = 1.2;
    private const double DefaultZoom = 1.3;
    private const double HalfCrop = 0.5 / DefaultZoom;

    public static ZoomSegment[] Plan(
        IEnumerable<PointerSample> samples, double duration, double start = 0, bool allowEndCut = true)
    {
        if (!double.IsFinite(start) || !double.IsFinite(duration) || start < 0 || duration <= start)
            return [];

        var clicks = samples.Where(sample => sample is { Click: true } &&
            sample.Time >= start && sample.Time < duration && sample.X is >= 0 and <= 1 &&
            sample.Y is >= 0 and <= 1).OrderBy(sample => sample.Time).ToArray();
        var segments = new List<ZoomSegment>();
        var earliestStart = start;

        for (var index = 0; index < clicks.Length;)
        {
            var first = clicks[index++];
            var last = first;
            var minX = first.X;
            var maxX = first.X;
            var minY = first.Y;
            var maxY = first.Y;
            while (index < clicks.Length)
            {
                var next = clicks[index];
                if (next.Time - last.Time > ClickGap ||
                    Math.Max(maxX, next.X) - Math.Min(minX, next.X) > MaximumRegionSpan ||
                    Math.Max(maxY, next.Y) - Math.Min(minY, next.Y) > MaximumRegionSpan)
                    break;
                last = next;
                index++;
                minX = Math.Min(minX, last.X);
                maxX = Math.Max(maxX, last.X);
                minY = Math.Min(minY, last.Y);
                maxY = Math.Max(maxY, last.Y);
            }

            var shotStart = Math.Max(earliestStart, first.Time - EaseInSeconds);
            earliestStart = last.Time;
            if (last.Time - first.Time < MinimumActionSpan)
                continue;

            if (segments.Count > 0)
                shotStart = Math.Max(shotStart, segments[^1].End + MinimumOverviewSeconds);
            var naturalEnd = last.Time + HoldSeconds + EaseOutSeconds;
            var nextClick = index < clicks.Length ? clicks[index].Time : duration;
            var shotEnd = Math.Min(naturalEnd, nextClick);
            // At an edit cut, keep the easing curve running until the picture ends.
            var curveEnd = allowEndCut && index == clicks.Length ? naturalEnd : shotEnd;
            if (shotStart > first.Time + 0.2 || shotEnd - shotStart < EaseInSeconds + 0.4 ||
                curveEnd - EaseOutSeconds < Math.Max(shotStart + EaseInSeconds + 0.4, last.Time + 0.25))
                continue;

            var x = Math.Clamp((minX + maxX) / 2, HalfCrop, 1 - HalfCrop);
            var y = Math.Clamp((minY + maxY) / 2, HalfCrop, 1 - HalfCrop);
            segments.Add(new ZoomSegment(shotStart, shotEnd, DefaultZoom, x, y)
            {
                UsesGentleMotion = true,
                CurveEnd = curveEnd > shotEnd ? curveEnd : null
            });
        }
        return segments.ToArray();
    }

    public static ZoomViewport At(IReadOnlyList<ZoomSegment> segments, double sourceSeconds)
    {
        var low = 0;
        var high = segments.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var segment = segments[middle];
            if (sourceSeconds < segment.Start)
                high = middle - 1;
            else if (sourceSeconds >= segment.End)
                low = middle + 1;
            else
                return AtSegment(segment, sourceSeconds);
        }
        return new ZoomViewport(1, 0.5, 0.5);
    }

    public static bool IsValid(IReadOnlyList<ZoomSegment> segments)
    {
        var previousEnd = 0.0;
        foreach (var segment in segments)
        {
            if (segment is null || !double.IsFinite(segment.Start) || !double.IsFinite(segment.End) ||
                segment.Start < previousEnd || segment.End <= segment.Start ||
                !(segment.Zoom is >= 1 and <= 2) || !(segment.X is >= 0 and <= 1) ||
                !(segment.Y is >= 0 and <= 1))
                return false;
            var curveStart = segment.CurveStart ?? segment.Start;
            var curveEnd = segment.CurveEnd ?? segment.End;
            if (!double.IsFinite(curveStart) || !double.IsFinite(curveEnd) ||
                curveStart < 0 || curveStart > segment.Start || curveEnd < segment.End)
                return false;
            var previousTime = -1.0;
            foreach (var point in segment.Focus ?? [])
            {
                if (point is null || !double.IsFinite(point.Time) || point.Time < 0 ||
                    point.Time <= previousTime || !(point.X is >= 0 and <= 1) || !(point.Y is >= 0 and <= 1))
                    return false;
                previousTime = point.Time;
            }
            previousEnd = segment.End;
        }
        return true;
    }

    private static ZoomViewport AtSegment(ZoomSegment segment, double time)
    {
        var start = segment.CurveStart ?? segment.Start;
        var end = segment.CurveEnd ?? segment.End;
        var ramp = Math.Min(TransitionSeconds, (end - start) / 2);
        var amount = segment.UsesGentleMotion
            ? SmoothStep(Math.Min((time - start) / EaseInSeconds, (end - time) / EaseOutSeconds))
            : Spring(Math.Min(time - start, end - time) / ramp);
        var zoom = 1 + (segment.Zoom - 1) * amount;
        var x = segment.X;
        var y = segment.Y;
        if (segment.Focus is { Length: > 1 } focus)
        {
            var low = 1;
            var high = focus.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (focus[middle].Time <= time)
                    low = middle + 1;
                else
                    high = middle;
            }
            var previous = focus[low - 1];
            x = previous.X;
            y = previous.Y;
            if (low < focus.Length)
            {
                var next = focus[low];
                var shift = Math.Min(TransitionSeconds, next.Time - previous.Time);
                var progress = Spring(Math.Clamp(1 + (time - next.Time) / shift, 0, 1));
                x += (next.X - x) * progress;
                y += (next.Y - y) * progress;
            }
        }
        var inset = 0.5 / zoom;
        return new ZoomViewport(zoom, Math.Clamp(x, inset, 1 - inset), Math.Clamp(y, inset, 1 - inset));
    }

    private static double Spring(double value)
    {
        if (value >= 1)
            return 1;
        return Math.Clamp(1 - Math.Exp(-7 * value) * (1 + 7 * value), 0, 1);
    }

    private static double SmoothStep(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * t * (10 + t * (6 * t - 15));
    }
}
