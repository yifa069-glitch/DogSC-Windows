using DogSC.Windows.Services;

internal static class RecordingOutputTests
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dogsc-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var settings = Path.Combine(directory, "settings", "recording-output.txt");
            var recovery = Path.Combine(directory, "recovery", "autosave.dogscproj");
            using (var owner = ProjectFileService.TryAcquireRecoveryLease(recovery))
            {
                Check(owner is not null, "First window can own recovery");
                File.WriteAllText(recovery, "current project");
                using var other = ProjectFileService.TryAcquireRecoveryLease(recovery);
                Check(other is null && File.ReadAllText(recovery) == "current project",
                    "Another window cannot claim recovery while the owner is open");
            }
            using var next = ProjectFileService.TryAcquireRecoveryLease(recovery);
            Check(next is not null && File.ReadAllText(recovery) == "current project",
                "A new window can use the previous owner's recovery after it closes");
            var original = Path.Combine(directory, "录屏 示例.mp4");
            Check(RecordingOutput.LoadPreference(settings) is null, "No default setting on first use");
            RecordingOutput.SavePreference(settings, original);
            Check(RecordingOutput.LoadPreference(settings) == original, "Full path survives reload");

            Check(RecordingOutput.ReservePath(original) == original, "First recording uses configured name");
            File.WriteAllText(original, "existing recording");
            var second = Path.Combine(directory, "录屏 示例 (2).mp4");
            Check(RecordingOutput.ReservePath(original) == second, "Second recording uses a numbered name");
            var third = Path.Combine(directory, "录屏 示例 (3).pointer.json");
            var fourth = Path.Combine(directory, "录屏 示例 (4).markers.json");
            File.WriteAllText(third, "existing track");
            File.WriteAllText(fourth, "existing markers");
            Check(RecordingOutput.ReservePath(original) == Path.Combine(directory, "录屏 示例 (5).mp4"),
                "Existing sidecars also reserve their names");
            Check(File.ReadAllText(original) == "existing recording" &&
                File.ReadAllText(third) == "existing track" && File.ReadAllText(fourth) == "existing markers",
                "Existing media and sidecars are intact");
            Check(RecordingOutput.LoadPreference(settings) == original, "Fallback names do not change preference");

            var unavailable = Path.Combine(original, "test.mp4");
            try
            {
                RecordingOutput.ReservePath(unavailable);
                throw new InvalidOperationException("A file cannot serve as the output directory.");
            }
            catch (IOException) { }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Check(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException($"Recording output regression: {scenario}");
    }
}
