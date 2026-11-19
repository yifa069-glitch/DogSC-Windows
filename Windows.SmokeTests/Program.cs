using System.Diagnostics;
using System.Globalization;
using DogSC.Windows.Models;
using DogSC.Windows.Services;

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException("SMOKE TEST FAILED: " + message);
}

static async Task<(int ExitCode, string Output)> RunAsync(string exe, params string[] args)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }
    };
    foreach (var arg in args)
        process.StartInfo.ArgumentList.Add(arg);

    if (!process.Start())
        throw new InvalidOperationException("Unable to start " + exe);

    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return (process.ExitCode, (await stdout) + Environment.NewLine + (await stderr));
}

Console.WriteLine("Window-capture regression check: use desktop crop instead of title= input.");
AutoZoomPlanTests.Run();
RecordingOutputTests.Run();
ManualZoomTests.Run();
TimelineLayoutTests.Run();
var windowCaptureArgs = new List<string>();
RecordingService.BuildArguments(new RecordingOptions
{
    OutputPath = Path.Combine(Path.GetTempPath(), "dogsc-window-capture-regression.mp4"),
    Mode = CaptureMode.Window,
    WindowTitle = "GPU accelerated test window",
    WindowX = 137,
    WindowY = 83,
    WindowWidth = 1280,
    WindowHeight = 720
}, windowCaptureArgs, "h264_mf");
Require(windowCaptureArgs.Contains("desktop"), "window capture must use the composed desktop input.");
Require(!windowCaptureArgs.Any(argument => argument.StartsWith("title=", StringComparison.OrdinalIgnoreCase)),
    "window capture must not use gdigrab title= because it can produce black GPU-window recordings.");
Require(windowCaptureArgs.IndexOf("-offset_x") >= 0 && windowCaptureArgs[windowCaptureArgs.IndexOf("-offset_x") + 1] == "137",
    "window capture X offset was not applied.");
Require(windowCaptureArgs.IndexOf("-offset_y") >= 0 && windowCaptureArgs[windowCaptureArgs.IndexOf("-offset_y") + 1] == "83",
    "window capture Y offset was not applied.");
Require(windowCaptureArgs.IndexOf("-video_size") >= 0 && windowCaptureArgs[windowCaptureArgs.IndexOf("-video_size") + 1] == "1280x720",
    "window capture crop dimensions were not applied.");

Console.WriteLine("Camera-unavailable regression check: recording continues without a camera device.");
var noCameraOptions = new RecordingOptions
{
    OutputPath = Path.Combine(Path.GetTempPath(), "dogsc-no-camera-regression.mp4"),
    CaptureCamera = true,
    CameraDevice = null
};
noCameraOptions.Validate();
var noCameraArgs = new List<string>();
RecordingService.BuildArguments(noCameraOptions, noCameraArgs, "h264_mf");
Require(noCameraArgs.Contains("desktop"), "no-camera recording must keep the desktop video input.");
Require(!noCameraArgs.Any(argument => argument.StartsWith("video=", StringComparison.OrdinalIgnoreCase)),
    "no-camera recording must not add an invalid DirectShow video input.");
Require(!noCameraArgs.Contains("-filter_complex"),
    "no-camera recording must use the regular screen-video filter, not a camera overlay graph.");

var ffmpeg = FfmpegLocator.FindFfmpegPath()
    ?? throw new InvalidOperationException("Set DOGSC_FFMPEG_PATH to the bundled ffmpeg.exe before running tests.");
var root = Path.Combine(Path.GetTempPath(), "DogSC-Integration-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var source = Path.Combine(root, "source.mp4");
    var output = Path.Combine(root, "edited.mp4");
    var audio = Path.Combine(root, "audio.m4a");
    var portrait = Path.Combine(root, "portrait.mp4");
    var square = Path.Combine(root, "square.mp4");
    var cameraOverlay = Path.Combine(root, "camera-overlay.mp4");
    var oddCapture = Path.Combine(root, "odd-window-capture.mp4");
    var oddCaptureFallback = Path.Combine(root, "odd-window-capture-fallback.mp4");
    var backgroundImage = Path.Combine(root, "background.png");
    var imageBackgroundOutput = Path.Combine(root, "image-background.mp4");
    var projectPath = Path.Combine(root, "roundtrip.dogscproj");

    Console.WriteLine("1/12 Generating a deterministic video+audio source.");
    var generate = await RunAsync(ffmpeg,
        "-hide_banner", "-y", "-loglevel", "error",
        "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30",
        "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
        "-t", "3",
        "-c:v", "h264_mf", "-b:v", "1M", "-pix_fmt", "nv12",
        "-c:a", "aac", "-b:a", "96k", "-shortest", source);
    Require(generate.ExitCode == 0 && File.Exists(source) && new FileInfo(source).Length > 1024,
        "could not generate source media: " + generate.Output);

    Console.WriteLine("Checking marker conflicts and recovery of captured audio after a failed mux.");
    var recording = Path.Combine(root, "finalization.mp4");
    var videoOnly = await RunAsync(ffmpeg, "-hide_banner", "-loglevel", "error", "-i", source, "-an", "-c:v", "copy", recording);
    Require(videoOnly.ExitCode == 0, "creating a silent capture failed: " + videoOnly.Output);
    var markerPath = Path.ChangeExtension(recording, ".markers.json");
    var markers = new[] { new RecordingMarker(1, 0.5, "marker", DateTimeOffset.UtcNow) };
    Directory.CreateDirectory(markerPath);
    var markerWarning = await RecordingService.SaveMarkersAsync(recording, markers);
    Require(markerWarning?.Contains(markerPath) == true && Directory.Exists(markerPath),
        "marker directory conflicts must be nonfatal and identify the destination.");
    Directory.Delete(markerPath);
    File.WriteAllText(markerPath, "existing markers");
    markerWarning = await RecordingService.SaveMarkersAsync(recording, markers);
    Require(markerWarning?.Contains(markerPath) == true && File.ReadAllText(markerPath) == "existing markers",
        "a newly appeared marker sidecar must survive finalization.");
    File.Delete(markerPath);
    Require(await RecordingService.SaveMarkersAsync(recording, markers) is null &&
        System.Text.Json.JsonSerializer.Deserialize<RecordingMarker[]>(File.ReadAllText(markerPath))?.Single() == markers[0],
        "normal marker finalization must save the original marker.");
    var loopback = Path.Combine(root, "loopback.wav");
    var generateWav = await RunAsync(ffmpeg, "-hide_banner", "-loglevel", "error", "-i", source, "-vn", loopback);
    Require(generateWav.ExitCode == 0, "creating a loopback fixture failed: " + generateWav.Output);
    var wavBytes = File.ReadAllBytes(loopback);
    using (var recorder = new RecordingService())
    {
        using (File.Open(recording, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try
            {
                await recorder.MergeCapturedAudioAsync(recording, loopback);
                throw new InvalidOperationException("A locked video must prevent replacement.");
            }
            catch (InvalidOperationException error) when (error.Message.Contains(loopback) && error.Message.Contains(recording))
            {
                Require(File.ReadAllBytes(loopback).SequenceEqual(wavBytes), "failed mux changed the only captured WAV.");
            }
        }
        await recorder.MergeCapturedAudioAsync(recording, loopback);
        Require(!File.Exists(loopback) && (await new MediaEditorService().ProbeAsync(recording)).HasAudio,
            "successful mux should contain system audio and remove the temporary WAV.");
    }

    Console.WriteLine("2/12 Verifying odd-sized window capture is padded for H.264.");
    const string evenSizeFilter = "pad=ceil(iw/2)*2:ceil(ih/2)*2:0:0:color=black,format=nv12";
    var oddCaptureResult = await RunAsync(ffmpeg,
        "-hide_banner", "-y", "-loglevel", "error",
        "-f", "lavfi", "-i", "testsrc=size=1600x1075:rate=30,format=bgra",
        "-t", "1", "-vf", evenSizeFilter,
        "-c:v", "h264_mf", "-b:v", "1M", "-pix_fmt", "nv12", oddCapture);
    Require(oddCaptureResult.ExitCode == 0 && File.Exists(oddCapture) && new FileInfo(oddCapture).Length > 1024,
        "odd-sized H.264 capture failed after even-size padding: " + oddCaptureResult.Output);
    var oddCaptureInfo = await new MediaEditorService().ProbeAsync(oddCapture);
    Require(oddCaptureInfo.Width == 1600 && oddCaptureInfo.Height == 1076,
        $"Odd-size capture should be padded to 1600x1076, got {oddCaptureInfo.Width}x{oddCaptureInfo.Height}.");

    Console.WriteLine("3/12 Verifying compatibility encoder fallback for odd-sized capture.");
    var oddFallbackResult = await RunAsync(ffmpeg,
        "-hide_banner", "-y", "-loglevel", "error",
        "-f", "lavfi", "-i", "testsrc=size=1600x1075:rate=30,format=bgra",
        "-t", "1", "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2:0:0:color=black,format=yuv420p",
        "-c:v", "mpeg4", "-b:v", "1M", "-pix_fmt", "yuv420p", oddCaptureFallback);
    Require(oddFallbackResult.ExitCode == 0 && File.Exists(oddCaptureFallback) && new FileInfo(oddCaptureFallback).Length > 1024,
        "MPEG-4 fallback failed for odd-sized capture: " + oddFallbackResult.Output);

    Console.WriteLine("4/12 Verifying the webcam overlay filter with synthetic camera input.");
    const string overlayGraph = "[1:v:0][0:v:0]scale2ref=w=main_w*0.22:h=main_h*0.22:force_original_aspect_ratio=decrease[cam][screen];" +
        "[screen][cam]overlay=x=main_w-overlay_w-32:y=main_h-overlay_h-32:eof_action=pass:shortest=0:repeatlast=1[vout]";
    var overlay = await RunAsync(ffmpeg,
        "-hide_banner", "-y", "-loglevel", "error",
        "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30",
        "-f", "lavfi", "-i", "testsrc2=size=640x480:rate=30",
        "-t", "1", "-filter_complex", overlayGraph,
        "-map", "[vout]", "-an", "-c:v", "h264_mf", "-b:v", "1M", "-pix_fmt", "nv12", cameraOverlay);
    Require(overlay.ExitCode == 0 && File.Exists(cameraOverlay) && new FileInfo(cameraOverlay).Length > 1024,
        "webcam overlay filter failed: " + overlay.Output);
    var overlayInfo = await new MediaEditorService().ProbeAsync(cameraOverlay);
    Require(overlayInfo.Width == 1280 && overlayInfo.Height == 720,
        $"Webcam overlay changed the screen canvas to {overlayInfo.Width}x{overlayInfo.Height}.");

    var editor = new MediaEditorService();
    Console.WriteLine("5/12 Probing the generated media.");
    var sourceInfo = await editor.ProbeAsync(source);
    Require(sourceInfo.HasVideo && sourceInfo.HasAudio && sourceInfo.DurationSeconds >= 2.8,
        "probe did not find the expected video/audio streams and duration.");

    var clip = new ClipItem
    {
        FilePath = source,
        StartSeconds = 0.25,
        EndSeconds = 2.75,
        Speed = 1.25
    };

    Console.WriteLine("6/12 Creating a custom background image.");
    var imageResult = await RunAsync(ffmpeg, "-hide_banner", "-y", "-loglevel", "error",
        "-f", "lavfi", "-i", "color=c=red:size=640x360", "-frames:v", "1", backgroundImage);
    Require(imageResult.ExitCode == 0 && File.Exists(backgroundImage), "could not generate background image: " + imageResult.Output);

    Console.WriteLine("7/12 Saving and reopening a project.");
    var projectService = new ProjectFileService();
    await projectService.SaveAsync(projectPath, new[] { clip });
    var reopened = await projectService.LoadAsync(projectPath);
    Require(reopened.Count == 1, "project did not reopen one clip.");
    Require(Math.Abs(reopened[0].StartSeconds - 0.25) < 0.001, "project in-point changed during round-trip.");
    Require(Math.Abs(reopened[0].EndSeconds - 2.75) < 0.001, "project out-point changed during round-trip.");
    Require(Math.Abs(reopened[0].Speed - 1.25) < 0.001, "project speed changed during round-trip.");

    Console.WriteLine("8/12 Exporting an edited MP4.");
    await editor.ExportAsync(reopened, output, 1080, 30, false);
    var exportedInfo = await editor.ProbeAsync(output);
    Require(exportedInfo.HasVideo && exportedInfo.HasAudio, "MP4 export is missing video or audio.");
    Require(exportedInfo.Width == 1920 && exportedInfo.Height == 1080,
        $"MP4 export has unexpected resolution {exportedInfo.Width}x{exportedInfo.Height}.");
    Require(exportedInfo.DurationSeconds > 1.7 && exportedInfo.DurationSeconds < 2.3,
        $"MP4 export has unexpected duration {exportedInfo.DurationSeconds.ToString(CultureInfo.InvariantCulture)}.");

    Console.WriteLine("9/12 Exporting a portrait canvas with fit padding.");
    await editor.ExportAsync(reopened, portrait, 1080, 30, false, null, "9:16", "fit", "dark");
    var portraitInfo = await editor.ProbeAsync(portrait);
    Require(portraitInfo.Width == 1080 && portraitInfo.Height == 1920,
        $"Portrait export has unexpected resolution {portraitInfo.Width}x{portraitInfo.Height}.");

    Console.WriteLine("10/12 Exporting a square canvas with fill/crop.");
    await editor.ExportAsync(reopened, square, 1080, 30, false, null, "1:1", "fill", "blue");
    var squareInfo = await editor.ProbeAsync(square);
    Require(squareInfo.Width == 1080 && squareInfo.Height == 1080,
        $"Square export has unexpected resolution {squareInfo.Width}x{squareInfo.Height}.");

    Console.WriteLine("11/12 Exporting with a custom background image.");
    await editor.ExportAsync(reopened, imageBackgroundOutput, 1080, 30, false, null, "16:9", "fit", "black", backgroundImage);
    var imageBackgroundInfo = await editor.ProbeAsync(imageBackgroundOutput);
    Require(imageBackgroundInfo.Width == 1920 && imageBackgroundInfo.Height == 1080,
        $"Image background export has unexpected resolution {imageBackgroundInfo.Width}x{imageBackgroundInfo.Height}.");

    Console.WriteLine("12/12 Exporting M4A audio.");
    await editor.ExportAsync(reopened, audio, 1080, 30, true);
    var audioInfo = await editor.ProbeAsync(audio);
    Require(audioInfo.HasAudio && !audioInfo.HasVideo, "M4A export must contain audio only.");
    Require(audioInfo.DurationSeconds > 1.7 && audioInfo.DurationSeconds < 2.3,
        $"M4A export has unexpected duration {audioInfo.DurationSeconds.ToString(CultureInfo.InvariantCulture)}.");

    Console.WriteLine("Auto-zoom: checking crop pixels at 30 and 60 FPS after project reload, trim, reorder, and speed changes.");
    var zoomSource = Path.Combine(root, "zoom-source.mp4");
    var zoomSourceResult = await RunAsync(ffmpeg, "-hide_banner", "-loglevel", "error",
        "-f", "lavfi", "-i", "color=black:size=640x360:rate=30:duration=8," +
        "drawbox=x=96:y=52:w=64:h=40:color=red:t=fill," +
        "drawbox=x=480:y=268:w=64:h=40:color=lime:t=fill",
        "-c:v", "mpeg4", "-q:v", "2", zoomSource);
    Require(zoomSourceResult.ExitCode == 0, "could not create coordinate-marker video: " + zoomSourceResult.Output);

    var zoomSegments = AutoZoom.Plan([
        new(1, 0.2, 0.2, true), new(1.5, 0.2, 0.2, true),
        new(5, 0.8, 0.8, true), new(5.5, 0.8, 0.8, true)
    ], 8);
    Require(zoomSegments.Length == 2, "the frame fixture must exercise both generated shots.");
    Console.WriteLine("Manual zoom: checking constant and animated centered crops after trim and speed changes.");
    foreach (var fps in new[] { 30, 60 })
    {
        var manual = new ClipItem { FilePath = zoomSource, StartSeconds = 1, EndSeconds = 3,
            Speed = 2, ZoomStart = fps == 30 ? 1.4 : 1, ZoomEnd = 1.4 };
        var outputFile = Path.Combine(root, $"manual-{fps}.mp4");
        await editor.ExportAsync([manual], outputFile, 1080, fps, false);
        foreach (var frame in new[] { 0, fps / 2, fps - 2 })
        {
            var sourceTime = manual.StartSeconds + (double)frame / fps * manual.Speed;
            await CheckMarkerAsync(outputFile, frame, sourceTime, red: true,
                expected: new ZoomViewport(manual.ManualZoomAt(sourceTime), 0.5, 0.5));
        }
    }
    var full = MakeZoomClip(0, 8, 1);
    var zoom30 = Path.Combine(root, "zoom-30.mp4");
    await editor.ExportAsync([full], zoom30, 1080, 30, false);
    await CheckMarkerAsync(zoom30, 15, 0.5, red: true);
    await CheckMarkerAsync(zoom30, 36, 1.2, red: true);
    await CheckMarkerAsync(zoom30, 51, 1.7, red: true);
    await CheckMarkerAsync(zoom30, 132, 4.4, red: false);
    await CheckMarkerAsync(zoom30, 171, 5.7, red: false);
    await CheckMarkerAsync(zoom30, 210, 7, red: false);

    var zoom60 = Path.Combine(root, "zoom-60.mp4");
    await projectService.SaveAsync(projectPath, [MakeZoomClip(3.25, 6.25, 1.5), MakeZoomClip(0.5, 3.5, 1.5)]);
    var savedZoom = await projectService.LoadAsync(projectPath);
    Require(savedZoom.All(item => item.AutoZoomEnabled && item.AutoZoomSegments.SequenceEqual(zoomSegments)),
        "project reload lost automatic camera segments.");
    await editor.ExportAsync(savedZoom, zoom60, 1080, 60, false);
    Require(Math.Abs((await editor.ProbeAsync(zoom60)).DurationSeconds - 4) < 0.05,
        "auto-zoom export changed edited duration.");
    await CheckMarkerAsync(zoom60, 46, 4.4, red: false);
    await CheckMarkerAsync(zoom60, 98, 5.7, red: false);
    await CheckMarkerAsync(zoom60, 168, 1.7, red: true);

    ClipItem MakeZoomClip(double start, double end, double speed) => new()
    {
        FilePath = zoomSource,
        StartSeconds = start,
        EndSeconds = end,
        Speed = speed,
        AutoZoomEnabled = true,
        AutoZoomSegments = zoomSegments
    };

    async Task CheckMarkerAsync(string video, int frame, double sourceTime, bool red, ZoomViewport? expected = null)
    {
        const int width = 160;
        const int height = 90;
        var samples = Path.Combine(root, "marker.rgb");
        var extract = await RunAsync(ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-i", video,
            "-vf", $"select=eq(n\\,{frame}),scale={width}:{height},format=rgb24",
            "-frames:v", "1", "-f", "rawvideo", samples);
        Require(extract.ExitCode == 0, "could not read rendered frame: " + extract.Output);
        var pixels = await File.ReadAllBytesAsync(samples);
        Require(pixels.Length == width * height * 3, $"output is missing frame {frame}.");
        var minX = width;
        var maxX = -1;
        var minY = height;
        var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 3;
                if (pixels[offset + (red ? 0 : 1)] < 140 || pixels[offset + (red ? 1 : 0)] > 70)
                    continue;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }
        var viewport = expected ?? AutoZoom.At(zoomSegments, sourceTime);
        var position = red ? 0.2 : 0.8;
        var expectedX = width * (0.5 + (position - viewport.X) * viewport.Zoom) - 0.5;
        var expectedY = height * (0.5 + (position - viewport.Y) * viewport.Zoom) - 0.5;
        Require(maxX >= minX && Math.Abs((minX + maxX) / 2d - expectedX) <= 2 &&
            Math.Abs((minY + maxY) / 2d - expectedY) <= 2 && Math.Abs(maxX - minX + 1 - 16 * viewport.Zoom) <= 2,
            $"zoom frame {frame} has marker ({minX}, {minY})–({maxX}, {maxY}), " +
            $"expected center ({expectedX:F1}, {expectedY:F1}) and width {16 * viewport.Zoom:F1}.");
    }

    Console.WriteLine("PASS: window-capture arguments, no-camera recording fallback, odd-sized capture padding, H.264 and MPEG-4 encoders, webcam overlay filter, media probing, project round-trip, edited MP4, portrait fit, square fill/crop, custom background, M4A export, and auto-zoom pixel/time alignment.");
}
finally
{
    try { Directory.Delete(root, recursive: true); }
    catch { Console.Error.WriteLine("Warning: could not clean temporary test directory " + root); }
}
