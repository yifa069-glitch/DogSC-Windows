using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DogSC.Windows.Models;

namespace DogSC.Windows.Services;

// WH_MOUSE_LL is delivered to the installing thread. A dedicated message pump
// keeps mouse delivery independent of the editor and FFmpeg.
internal sealed class PointerTrackRecorder : IDisposable
{
    private const int MouseHook = 14;
    private const int MouseMove = 0x0200;
    private const int LeftButtonDown = 0x0201;
    private const int RightButtonDown = 0x0204;
    private const uint QuitMessage = 0x0012;
    private readonly List<PointerSample> _samples = new();
    private readonly Thread _thread;
    private readonly HookDelegate _callback;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly (int Left, int Top, int Width, int Height) _fallbackBounds;
    private uint _threadId;
    private long _startedAt;
    private double _unixStart;
    private double _lastMove = double.NegativeInfinity;
    private double _firstFrameOffset;
    private bool _screenInput;
    private (int Left, int Top, int Width, int Height)? _actualBounds;

    public PointerTrackRecorder(RecordingOptions options)
    {
        _fallbackBounds = options.Mode switch
        {
            CaptureMode.Region => (options.RegionX, options.RegionY, options.RegionWidth, options.RegionHeight),
            CaptureMode.Window => (options.WindowX, options.WindowY, options.WindowWidth, options.WindowHeight),
            _ => (GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79))
        };
        _callback = HandleMouse;
        _thread = new Thread(Capture) { IsBackground = true, Name = "DogSC mouse recording" };
        _thread.Start();
    }

    public Task<bool> Ready => _ready.Task;

    public void Begin()
    {
        _unixStart = (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).TotalSeconds;
        Volatile.Write(ref _startedAt, Stopwatch.GetTimestamp());
    }

    public void ReadCaptureLog(string line)
    {
        var bounds = Regex.Match(line, @"capturing (?:whole desktop as )?(\d+)x(\d+)x\d+ at \((-?\d+),(-?\d+)\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (bounds.Success &&
            int.TryParse(bounds.Groups[1].Value, out var width) && int.TryParse(bounds.Groups[2].Value, out var height) &&
            int.TryParse(bounds.Groups[3].Value, out var left) && int.TryParse(bounds.Groups[4].Value, out var top))
            _actualBounds = (left, top, width, height);

        if (line.StartsWith("Input #", StringComparison.Ordinal))
            _screenInput = line.StartsWith("Input #0, gdigrab", StringComparison.Ordinal);
        if (_screenInput && line.TrimStart().StartsWith("Duration:", StringComparison.Ordinal))
        {
            // GDIgrab stamps its first video frame with Unix time before the blit.
            var timestamp = Regex.Match(line, @"\bstart:\s*(\d+\.\d+)");
            if (timestamp.Success && double.TryParse(timestamp.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var firstFrame))
            {
                var offset = firstFrame - _unixStart;
                if (offset is >= 0 and < 60)
                    _firstFrameOffset = offset;
            }
        }
    }

    public IReadOnlyList<PointerSample> GetNormalizedSamples()
    {
        var (left, top, width, height) = _actualBounds ?? _fallbackBounds;
        // The recording filter pads odd frame dimensions to even dimensions.
        double paddedWidth = width + (width & 1);
        double paddedHeight = height + (height & 1);
        return _samples.Where(sample => sample.Time >= _firstFrameOffset &&
                                        sample.X >= left && sample.X < left + (double)width &&
                                        sample.Y >= top && sample.Y < top + (double)height)
            .Select(sample => new PointerSample(sample.Time - _firstFrameOffset,
                (sample.X - left) / paddedWidth, (sample.Y - top) / paddedHeight, sample.Click))
            .ToArray();
    }

    private void Capture()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, 0);
        var hook = SetWindowsHookEx(MouseHook, _callback, GetModuleHandle(null), 0);
        _ready.SetResult(hook != 0);
        if (hook == 0)
            return;
        try
        {
            // A low-level hook is dispatched while GetMessage services this queue.
            while (GetMessage(out _, 0, 0, 0) > 0) { }
        }
        finally
        {
            UnhookWindowsHookEx(hook);
        }
    }

    private nint HandleMouse(int code, nint message, nint data)
    {
        var startedAt = Volatile.Read(ref _startedAt);
        bool click = message is LeftButtonDown or RightButtonDown;
        if (startedAt != 0 && code >= 0 && (click || message == MouseMove))
        {
            var time = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
            if (click || time - _lastMove >= 1.0 / 30)
            {
                // POINT is the first field of MSLLHOOKSTRUCT, in physical screen pixels.
                var point = Marshal.PtrToStructure<Point>(data);
                _samples.Add(new PointerSample(time, point.X, point.Y, click));
                _lastMove = time;
            }
        }
        return CallNextHookEx(0, code, message, data);
    }

    public void Dispose()
    {
        Volatile.Write(ref _startedAt, 0);
        if (_thread.IsAlive)
        {
            PostThreadMessage(_threadId, QuitMessage, 0, 0);
            _thread.Join();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public Point Position;
        public uint Private;
    }

    private delegate nint HookDelegate(int code, nint message, nint data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SetWindowsHookEx(int hook, HookDelegate callback, nint module, uint threadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
