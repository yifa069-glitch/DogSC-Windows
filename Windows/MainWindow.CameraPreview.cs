using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DogSC.Windows.Services;

namespace DogSC.Windows;

public partial class MainWindow
{
    private readonly DispatcherTimer _cameraPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly byte[] _cameraPreviewPixels = new byte[CameraPreviewStream.FrameSize];
    private CameraPreviewStream? _cameraPreview;
    private string? _cameraPreviewDevice;
    private Task _cameraPreviewStopping = Task.CompletedTask;
    private WriteableBitmap? _cameraPreviewBitmap;
    private long _cameraPreviewLastFrame;
    private long? _cameraPreviewRetrySince;

    private void UpdateCameraPreview()
    {
        var device = MainTabs?.SelectedIndex == 0 && CameraOverlayCheckBox?.IsChecked == true &&
                     StartRecordingButton?.IsEnabled == true && !_recordingService.IsRecording
            ? CameraCombo.SelectedItem as string
            : null;
        if (string.IsNullOrWhiteSpace(device))
        {
            _cameraPreviewTimer.Stop();
            _cameraPreviewRetrySince = null;
            _ = StopCameraPreviewAsync();
            return;
        }

        _cameraPreviewTimer.Start();
        if (_cameraPreview is not null && !string.Equals(_cameraPreviewDevice, device, StringComparison.Ordinal))
        {
            _cameraPreviewRetrySince = null;
            _ = StopCameraPreviewAsync();
        }
        if (!_cameraPreviewStopping.IsCompletedSuccessfully)
            return;

        if (_cameraPreview is null)
        {
            if (_cameraPreviewRetrySince is { } retry && Stopwatch.GetElapsedTime(retry).TotalSeconds < 2)
                return;

            try
            {
                var ffmpeg = FfmpegLocator.FindFfmpegPath();
                if (ffmpeg is null)
                {
                    _cameraPreviewTimer.Stop();
                    return;
                }
                _cameraPreview = new CameraPreviewStream(ffmpeg, device);
                _cameraPreviewDevice = device;
                _cameraPreviewLastFrame = Stopwatch.GetTimestamp();
            }
            catch (Exception exception)
            {
                App.WriteDiagnostic("Camera preview failed to start: " + exception.Message);
                _cameraPreviewRetrySince = Stopwatch.GetTimestamp();
                return;
            }
        }

        if (_cameraPreview.TryCopyFrame(_cameraPreviewPixels))
        {
            _cameraPreviewLastFrame = Stopwatch.GetTimestamp();
            _cameraPreviewBitmap ??= new WriteableBitmap(CameraPreviewStream.Width, CameraPreviewStream.Height,
                96, 96, PixelFormats.Bgr24, null);
            _cameraPreviewBitmap.WritePixels(new Int32Rect(0, 0, CameraPreviewStream.Width, CameraPreviewStream.Height),
                _cameraPreviewPixels, CameraPreviewStream.Stride, 0);
            if (CameraPreviewImage.Source != _cameraPreviewBitmap)
            {
                CameraPreviewImage.Source = _cameraPreviewBitmap;
                UpdateCameraPreviewPlacement();
                CameraPreviewFrame.Visibility = Visibility.Visible;
            }
        }
        else if (_cameraPreview.Completion.IsCompleted ||
                 Stopwatch.GetElapsedTime(_cameraPreviewLastFrame).TotalSeconds >= 6)
        {
            App.WriteDiagnostic("Camera preview stream stopped: " +
                (_cameraPreview.Error ?? _cameraPreview.Completion.Exception?.GetBaseException().Message ?? "未收到摄像头画面。"));
            _cameraPreviewRetrySince = Stopwatch.GetTimestamp();
            _ = StopCameraPreviewAsync();
        }
    }

    private Task StopCameraPreviewAsync()
    {
        if (CameraPreviewFrame is not null)
            CameraPreviewFrame.Visibility = Visibility.Collapsed;
        if (CameraPreviewImage is not null)
            CameraPreviewImage.Source = null;
        if (_cameraPreview is { } stream)
        {
            _cameraPreview = null;
            _cameraPreviewDevice = null;
            // FFmpeg recording opens its own DirectShow input, so wait for its preview owner to exit.
            _cameraPreviewStopping = stream.StopAsync();
        }
        return _cameraPreviewStopping;
    }
}
