using DogSC.Windows.Models;

internal static class AutoZoomPlanTests
{
    public static void Run()
    {
        Require(AutoZoom.Plan([new PointerSample(1, 0.5, 0.5, false)], 10).Length == 0);
        Require(AutoZoom.Plan([new PointerSample(1, -0.01, 0.5, true)], 10).Length == 0);

        Require(AutoZoom.Plan([new PointerSample(2, 0.5, 0.5, true)], 10).Length == 0);
        Require(AutoZoom.Plan([
            new PointerSample(2, 0.5, 0.5, true),
            new PointerSample(2.15, 0.5, 0.5, true)
        ], 10).Length == 0);

        var group = AutoZoom.Plan([
            new PointerSample(2, 0.1, 0.1, true),
            new PointerSample(2.5, 0.18, 0.12, true),
            new PointerSample(2.8, 0.95, 0.95, false),
            new PointerSample(3.5, 0.19, 0.13, true),
            new PointerSample(5, 0.1, 0.1, false),
            new PointerSample(6, 0.9, 0.9, false)
        ], 10);
        Require(group.Length == 1 && AutoZoom.IsValid(group));
        Require(group[0] is { Zoom: 1.3, Focus: null, UsesGentleMotion: true });
        Require(Math.Abs(group[0].Start - 1.05) < 0.001 && Math.Abs(group[0].End - 5.65) < 0.001);
        Require(AutoZoom.At(group, 1.06).Zoom - 1 < 0.00001);
        Require(AutoZoom.At(group, 1.525).Zoom is > 1.14 and < 1.16);
        Require(AutoZoom.At(group, 2.1) == AutoZoom.At(group, 4.49));
        Require(AutoZoom.At(group, 5.075).Zoom is > 1.14 and < 1.16);
        Require(AutoZoom.At(group, 5.64).Zoom - 1 < 0.00001);
        Require(AutoZoom.At(group, 5.66).Zoom == 1);

        var oppositeBurst = AutoZoom.Plan([
            new PointerSample(2, 0.02, 0.02, true),
            new PointerSample(2.3, 0.98, 0.98, true),
            new PointerSample(2.6, 0.02, 0.02, true)
        ], 10);
        Require(AutoZoom.At(oppositeBurst, 2).Zoom == 1 && AutoZoom.At(oppositeBurst, 2.6).Zoom == 1);

        var separated = AutoZoom.Plan([
            new PointerSample(2, 0.3, 0.3, true),
            new PointerSample(2.5, 0.3, 0.3, true),
            new PointerSample(5.8, 0.7, 0.7, true),
            new PointerSample(6.3, 0.7, 0.7, true)
        ], 12);
        Require(separated.Length == 2 && separated[1].Start - separated[0].End >= 1.2 - 0.001);
        Require(AutoZoom.At(separated, 5.1).Zoom == 1);

        foreach (var x in new[] { 0.0, 1.0 })
        foreach (var y in new[] { 0.0, 1.0 })
        {
            var corner = AutoZoom.At(AutoZoom.Plan([
                new PointerSample(2, x, y, true), new PointerSample(2.5, x, y, true)
            ], 10), 2.7);
            var inset = 0.5 / corner.Zoom;
            Require(corner.Zoom == 1.3);
            Require(corner.X >= inset && corner.X <= 1 - inset && corner.Y >= inset && corner.Y <= 1 - inset);
        }

        PointerSample[] lateClicks = [new(8.2, 0.5, 0.5, true), new(9, 0.5, 0.5, true)];
        var clipped = AutoZoom.Plan(lateClicks, 9.1);
        Require(clipped.Length == 1 && clipped[0] is { End: 9.1, CurveEnd: > 9.1 });
        Require(AutoZoom.At(clipped, 9.09).Zoom == 1.3);
        Require(AutoZoom.Plan(lateClicks, 9.1, allowEndCut: false).Length == 0);

        var legacy = new ZoomSegment(0, 4, 1.6, 0.5, 0.5);
        Require(AutoZoom.At([legacy], 0.35).Zoom is > 1.51 and < 1.53);
        Console.WriteLine("Auto zoom planning and edge checks passed.");
    }

    private static void Require(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Auto zoom plan check failed.");
    }
}
