using System.Diagnostics;
using System.IO;

namespace DogSC.Windows.Services;

public sealed class CameraPreviewStream
{
    public const int Width = 320;
    public const int Height = 180;
    public const int Stride = Width * 3;
    public const int FrameSize = Stride * Height;

    private readonly Process _process;
    private readonly object _frameLock = new();
    private byte[] _readBuffer = new byte[FrameSize];
    private byte[] _latestFrame = new byte[FrameSize];
    private bool _frameReady;

    public CameraPreviewStream(string ffmpegPath, string deviceName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-hide_banner", "-loglevel", "error", "-nostdin",
            "-f", "dshow", "-i", "video=" + deviceName,
            "-an", "-vf", $"fps=15,scale={Width}:{Height}:force_original_aspect_ratio=decrease,pad={Width}:{Height}:(ow-iw)/2:(oh-ih)/2",
            "-c:v", "rawvideo", "-pix_fmt", "bgr24", "-threads:v", "1", "-f", "rawvideo", "pipe:1"
        })
            startInfo.ArgumentList.Add(argument);

        _process = new Process { StartInfo = startInfo };
        try
        {
            if (!_process.Start())
                throw new InvalidOperationException("无法启动摄像头预览进程。");
        }
        catch
        {
            _process.Dispose();
            throw;
        }
        Completion = ReadFramesAsync();
    }

    public Task Completion { get; }
    public string? Error { get; private set; }

    public bool TryCopyFrame(byte[] destination)
    {
        lock (_frameLock)
        {
            if (!_frameReady)
                return false;
            _latestFrame.CopyTo(destination, 0);
            _frameReady = false;
            return true;
        }
    }

    private async Task ReadFramesAsync()
    {
        var stream = _process.StandardOutput.BaseStream;
        var errors = _process.StandardError.ReadToEndAsync();
        try
        {
            while (await stream.ReadAtLeastAsync(_readBuffer, FrameSize, false)
                       .ConfigureAwait(false) == FrameSize)
            {
                lock (_frameLock)
                {
                    (_readBuffer, _latestFrame) = (_latestFrame, _readBuffer);
                    _frameReady = true;
                }
            }
        }
        catch (IOException exception)
        {
            Error = exception.Message;
        }

        await _process.WaitForExitAsync().ConfigureAwait(false);
        var diagnostics = (await errors.ConfigureAwait(false)).Trim();
        if (diagnostics.Length > 0)
            Error = diagnostics;
    }

    public async Task StopAsync()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill();
        }
        catch (InvalidOperationException)
        {
            // The capture process can exit between checking and stopping it.
        }

        try
        {
            await Completion.ConfigureAwait(false);
        }
        finally
        {
            _process.Dispose();
        }
    }
}
