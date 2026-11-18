using DogSC.Windows.Models;

internal static class ManualZoomTests
{
    public static void Run()
    {
        var clip = new ClipItem { StartSeconds = 4, EndSeconds = 8, Speed = 2, ZoomStart = 2, ZoomEnd = 1 };
        if (clip.ManualZoomAt(3) != 2 || clip.ManualZoomAt(6) != 1.5 || clip.ManualZoomAt(9) != 1)
            throw new InvalidOperationException("Manual zoom must interpolate between the source in- and out-points.");
        clip.ZoomStart = clip.ZoomEnd = 1.5;
        if (clip.ManualZoomAt(7) != 1.5)
            throw new InvalidOperationException("Equal endpoints must keep the same zoom throughout the clip.");
    }
}
