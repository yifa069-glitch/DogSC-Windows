using System.IO;

namespace DogSC.Windows.Services;

public static class RecordingOutput
{
    public static string NormalizePath(string value)
    {
        var path = value.Trim();
        if (!Path.IsPathFullyQualified(path) ||
            !string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(path).Length == 0 ||
            Path.GetFileName(path).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("请输入完整的 MP4 输出路径，例如 C:\\Videos\\录屏.mp4。");

        return Path.GetFullPath(path);
    }

    public static string? LoadPreference(string settingsPath)
    {
        return File.Exists(settingsPath) ? NormalizePath(File.ReadAllText(settingsPath)) : null;
    }

    public static void SavePreference(string settingsPath, string path)
    {
        path = NormalizePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporaryPath = settingsPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, path);
            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    // Claim the file before FFmpeg opens it so two DogSC instances cannot choose the same name.
    public static string ReservePath(string preferredPath)
    {
        preferredPath = NormalizePath(preferredPath);
        var directory = Path.GetDirectoryName(preferredPath)!;
        var name = Path.GetFileNameWithoutExtension(preferredPath);
        var extension = Path.GetExtension(preferredPath);
        Directory.CreateDirectory(directory);
        for (var suffix = 1; ; suffix++)
        {
            var path = suffix == 1 ? preferredPath : Path.Combine(directory, $"{name} ({suffix}){extension}");
            if (Path.Exists(path) || Path.Exists(Path.ChangeExtension(path, ".markers.json")) ||
                Path.Exists(Path.ChangeExtension(path, ".pointer.json")))
                continue;

            try
            {
                using var reservation = File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return path;
            }
            catch (IOException error) when ((error.HResult & 0xffff) is 80 or 183)
            {
                // Another recording claimed this name after the existence checks.
            }
        }
    }
}
