namespace DogSC.Windows.Models;

public sealed class TimelineLayout
{
    public TimelineLayout(IEnumerable<ClipItem> clips)
    {
        var entries = new List<TimelineClip>();
        var start = 0.0;
        foreach (var clip in clips)
        {
            var entry = new TimelineClip(entries.Count, clip, start);
            entries.Add(entry);
            start = entry.End;
        }
        Clips = entries;
        Duration = start;
    }

    public IReadOnlyList<TimelineClip> Clips { get; }
    public double Duration { get; }

    public TimelineClip? Find(ClipItem clip) => Clips.FirstOrDefault(entry => ReferenceEquals(entry.Clip, clip));

    public TimelinePosition? Locate(double projectSeconds)
    {
        if (Duration <= 0 || !double.IsFinite(projectSeconds))
            return null;

        var time = Math.Clamp(projectSeconds, 0, Duration);
        if (time == Duration)
        {
            var last = Clips.Last(entry => entry.Duration > 0);
            return new TimelinePosition(last, last.SourceEnd);
        }

        foreach (var entry in Clips)
        {
            if (time < entry.End && entry.Duration > 0)
                return new TimelinePosition(entry, entry.SourceStart + (time - entry.Start) * entry.Speed);
        }
        return null;
    }
}

public sealed class TimelineClip
{
    internal TimelineClip(int index, ClipItem clip, double start)
    {
        Index = index;
        Clip = clip;
        Start = start;
        SourceStart = double.IsFinite(clip.StartSeconds) ? clip.StartSeconds : 0;
        SourceEnd = double.IsFinite(clip.EndSeconds) ? Math.Max(SourceStart, clip.EndSeconds) : SourceStart;
        Speed = Math.Max(0.01, clip.Speed);
    }

    public int Index { get; }
    public ClipItem Clip { get; }
    public double Start { get; }
    public double End => Start + Duration;
    public double Duration => (SourceEnd - SourceStart) / Speed;
    internal double SourceStart { get; }
    internal double SourceEnd { get; }
    internal double Speed { get; }

    public double ToProjectTime(double sourceSeconds) =>
        Start + (Math.Clamp(sourceSeconds, SourceStart, SourceEnd) - SourceStart) / Speed;

    public IEnumerable<TimelineZoom> GetZooms()
    {
        if (!Clip.AutoZoomEnabled || Duration <= 0)
            yield break;

        foreach (var segment in Clip.AutoZoomSegments)
        {
            if (segment is null)
                continue;
            var start = Math.Max(segment.Start, SourceStart);
            var end = Math.Min(segment.End, SourceEnd);
            if (end > start)
                yield return new TimelineZoom(segment, ToProjectTime(start), ToProjectTime(end));
        }
    }

    public IEnumerable<TimelineMarker> GetMarkers(IEnumerable<RecordingMarker> markers)
    {
        if (Duration <= 0)
            yield break;

        foreach (var marker in markers)
        {
            if (marker is not null && marker.TimeSeconds >= SourceStart && marker.TimeSeconds < SourceEnd)
                yield return new TimelineMarker(marker, ToProjectTime(marker.TimeSeconds));
        }
    }
}

public readonly record struct TimelinePosition(TimelineClip Entry, double SourceTime);
public readonly record struct TimelineZoom(ZoomSegment Segment, double Start, double End);
public readonly record struct TimelineMarker(RecordingMarker Marker, double Time);
