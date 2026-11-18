namespace DogSC.Windows.Models;

internal static class AutoZoomRegeneration
{
    public static ZoomSegment[] Replace(
        IReadOnlyList<PointerSample> samples, double start, double end, IReadOnlyList<ZoomSegment> existing)
    {
        var result = new List<ZoomSegment>();
        var availableStart = start;
        foreach (var segment in existing)
        {
            if (segment.End <= start || segment.Start >= end)
            {
                result.Add(segment);
                continue;
            }
            if (segment.IsManual)
            {
                AddAutomatic(availableStart, segment.Start);
                result.Add(segment);
                availableStart = Math.Max(availableStart, segment.End);
                continue;
            }

            if (segment.Start < start)
                result.Add(PreserveCurve(segment, segment.Start, start));
            if (segment.End > end)
                result.Add(PreserveCurve(segment, end, segment.End));
        }
        AddAutomatic(availableStart, end);
        return result.OrderBy(segment => segment.Start).ToArray();

        void AddAutomatic(double from, double to)
        {
            if (to > from)
                result.AddRange(AutoZoom.Plan(samples, to, from, allowEndCut: to == end));
        }
    }

    private static ZoomSegment PreserveCurve(ZoomSegment segment, double start, double end) =>
        segment with
        {
            Start = start,
            End = end,
            CurveStart = segment.CurveStart ?? segment.Start,
            CurveEnd = segment.CurveEnd ?? segment.End
        };
}
