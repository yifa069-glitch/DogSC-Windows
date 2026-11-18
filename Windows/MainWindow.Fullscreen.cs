using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DogSC.Windows;

public partial class MainWindow
{
    private Window? _fullscreenPreview;
    private TimeSpan? _previewTransferPosition;

    private void FullscreenPreview_Click(object sender, RoutedEventArgs e) => OpenFullscreenPreview();

    private void OpenFullscreenPreview()
    {
        if (_fullscreenPreview is not null || _previewTransferPosition.HasValue)
            return;
        if (!_previewMediaReady || _previewClip is null || _previewElement is not { NaturalVideoWidth: > 0, NaturalVideoHeight: > 0 })
        {
            EditorStatusText.Text = "请先导入并选择一个可预览的片段。";
            return;
        }

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(16)
        };
        var play = new Button { Content = "播放 / 暂停", ToolTip = "空格", Margin = new Thickness(0, 0, 8, 0) };
        var exit = new Button { Content = "退出全屏", ToolTip = "Esc 或 F11" };
        controls.Children.Add(play);
        controls.Children.Add(exit);

        var stage = new Grid { Background = Brushes.Black };
        var window = new Window
        {
            Owner = this, Title = "DogSC · 全屏预览", Content = stage,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Background = Brushes.Black
        };
        play.Click += TogglePreview_Click;
        exit.Click += (_, _) => window.Close();
        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key is Key.Escape or Key.F11 or Key.Space)
            {
                args.Handled = true;
                if (args.IsRepeat)
                    return;
                if (args.Key == Key.Space)
                    TogglePreview_Click(this, new RoutedEventArgs());
                else
                    window.Close();
            }
        };
        window.Loaded += (_, _) => FillPreviewMonitor(window);
        window.Closing += (_, _) => BeginPreviewTransfer();
        window.Closed += (_, _) =>
        {
            stage.Children.Remove(PreviewViewport);
            PreviewFrame.Child = PreviewViewport;
            _fullscreenPreview = null;
            FinishPreviewTransfer(null);
        };

        // WPF keeps the decoder but can reset its position when the visual crosses HWNDs.
        BeginPreviewTransfer();
        PreviewFrame.Child = null;
        stage.Children.Add(PreviewViewport);
        stage.Children.Add(controls);
        _fullscreenPreview = window;
        window.Show();
        FinishPreviewTransfer(window);
    }

    private void BeginPreviewTransfer()
    {
        if (_previewTransferPosition.HasValue || _previewElement is not { } media)
            return;
        _previewTransferPosition = media.Position;
        _previewTimer.Stop();
        if (_previewPlaying)
            media.Pause();
    }

    private void FinishPreviewTransfer(Window? destination)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_fullscreenPreview != destination || _previewTransferPosition is not { } position ||
                _previewElement is not { } media)
                return;

            _previewTransferPosition = null;
            media.Position = position;
            FitPreviewFrame();
            ApplyPreviewZoom(position.TotalSeconds);
            if (_previewPlaying)
            {
                media.Play();
                _previewTimer.Start();
            }
        }, DispatcherPriority.ContextIdle);
    }

    private void FillPreviewMonitor(Window window)
    {
        const uint nearestMonitor = 2;
        const uint noZOrderOrActivation = 0x0014;
        var monitor = MonitorFromWindow(new WindowInteropHelper(this).Handle, nearestMonitor);
        var info = new PreviewMonitorInfo { Size = (uint)Marshal.SizeOf<PreviewMonitorInfo>() };
        if (GetMonitorInfo(monitor, ref info))
        {
            var bounds = info.Monitor;
            if (SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero,
                    bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, noZOrderOrActivation))
                return;
        }
        window.WindowState = WindowState.Maximized;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PreviewMonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref PreviewMonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
