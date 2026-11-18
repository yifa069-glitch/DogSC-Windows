using DogSC.Windows.Models;

internal static class TimelineLayoutTests
{
    public static void Run()
    {
        var zoom = new ZoomSegment(0, 5, 1.6, 0.5, 0.5);
        var first = new ClipItem
        {
            StartSeconds = 2, EndSeconds = 4, Speed = 2,
            AutoZoomEnabled = true, AutoZoomSegments = [zoom]
        };
        var second = new ClipItem { StartSeconds = 4, EndSeconds = 6, Speed = 0.5 };
        var empty = new ClipItem();
        var timeline = new TimelineLayout([first, second, empty]);
        Check(timeline.Duration == 5, "retimed project duration");
        Check(timeline.Locate(-5) is { Entry.Index: 0, SourceTime: 2 }, "clamp to project start");
        Check(timeline.Locate(0.5) is { Entry.Index: 0, SourceTime: 3 }, "source time after trim and speed");
        Check(timeline.Locate(1) is { Entry.Index: 1, SourceTime: 4 }, "cut belongs to the following clip");
        Check(timeline.Locate(5) is { Entry.Index: 1, SourceTime: 6 }, "total duration reaches the final playable frame position");
        Check(timeline.Locate(double.NaN) is null && new TimelineLayout([empty]).Locate(0) is null,
            "invalid and empty positions");
        Check(timeline.Find(second)?.ToProjectTime(5) == 3, "map second clip source time to project time");
        Check(new TimelineLayout([second, first]).Find(first)?.Start == 4, "reordered clip offset");

        var firstEntry = timeline.Clips[0];
        Check(firstEntry.GetZooms().Single() == new TimelineZoom(zoom, 0, 1), "zoom intersected with retained source range");
        first.AutoZoomEnabled = false;
        Check(!firstEntry.GetZooms().Any(), "disabled zoom hidden");
        RecordingMarker[] markers = [new(1, 1, "before", default), new(2, 2, "start", default),
            new(3, 3, "inside", default), new(4, 4, "boundary", default)];
        Check(firstEntry.GetMarkers(markers).Select(marker => marker.Time).SequenceEqual([0.0, 0.5]),
            "trimmed markers and split boundary");
        Check(timeline.Clips[1].GetMarkers(markers).Single().Time == 1, "boundary marker appears on next clip only");
        Console.WriteLine("Timeline duration, seeks, zoom spans and marker position checks passed.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException("Timeline layout check failed: " + description);
    }
}
