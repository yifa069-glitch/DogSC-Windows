# <img src="Windows/Assets/DogSC.png" width="40" height="40" alt=""> DogSC for Windows

[简体中文](README.md) · English

DogSC is a Windows desktop app for recording your screen and editing videos locally. It's designed for software tutorials. DogSC records mouse clicks so the editor can automatically zoom in on a series of actions and then smoothly return to the full view.

This repository maintains an enhanced Windows x64 edition. The original app was developed by 神烦老狗 (laogou717). The Windows edition builds on spring-it's `windows-v0.1.21` community port. [Original macOS project](https://github.com/laogou717/dogsc) · [Windows port baseline](https://github.com/spring-it/dogsc/releases/tag/windows-v0.1.21)

## Installation

DogSC supports 64-bit Windows 10 version 2004 (build 19041) or later. Download and run `DogSC-Setup-win-x64.exe`. The installer includes the .NET Desktop Runtime, FFmpeg, and ffprobe, and can create a desktop shortcut.

[Download the Windows x64 installer](https://github.com/yifa069-glitch/DogSC-Windows/releases/download/windows-v0.1.21.11/DogSC-Setup-win-x64.exe) · [v0.1.21.11 release notes](https://github.com/yifa069-glitch/DogSC-Windows/releases/tag/windows-v0.1.21.11) · [All releases](https://github.com/yifa069-glitch/DogSC-Windows/releases)

## Record and export a tutorial

1. Under **Settings & Help → Default Output File** (设置与帮助 → 默认输出文件), select an `.mp4` file or enter its full path. On first launch, DogSC suggests a timestamped filename. After you change the path, DogSC remembers both the folder and filename across restarts. If a recording or any of its companion files already exists at the chosen path, DogSC appends a number such as ` (2)` or ` (3)` to the new recording's name.
2. Return to **Record** (录制) and choose full-screen, window, or region capture. Enable system audio, your microphone, or a webcam overlay as needed. Keep the target content visible when capturing a window. Press `Ctrl+Alt+M` during recording to add a marker.
3. After stopping, click **Edit the Recording** (编辑刚录制的视频). Use the timeline at the bottom to seek, split, or rearrange clips. Switch between thumbnails and waveforms, and adjust speed and volume in the clip properties. Use **Full-Screen Preview** (全屏预览) to inspect details.
4. Open **Auto Zoom** (自动运镜) to adjust the focus point, zoom level, and timing for the selected clip or regenerate its zooms. When the preview looks right, click **Start Export** (开始导出) to export MP4 video or M4A audio. Press `Ctrl+S` to save your project and resume editing later.

If multiple DogSC instances are open, one instance handles automatic recovery. You can press `Ctrl+S` in each instance to save its project.

## Auto zoom and source files

The editor groups a series of clicks in the same area into a single shot with a fixed framing and zooms in gradually to **1.3×** by default. Isolated clicks and mouse movement alone keep the full view. Regenerating zooms affects only the selected clip from its in point to its out point and preserves manually added zoom segments. You can undo the change. The picture-in-picture webcam overlay is baked into the recording and zooms along with the rest of the image.

For example, `tutorial.mp4` is the playable full-view recording, `tutorial.pointer.json` stores the pointer track, and `tutorial.markers.json` stores recording markers. Keep the video and pointer track together if you move them and want to regenerate zooms later. You can also import an existing MP4 and add manual zooms if it has no accompanying pointer track. A `.dogscproj` file stores clip edits and zoom settings. When you reopen a project, DogSC needs access to the original media at the saved paths.

## Development and feedback

DogSC uses C#, WPF, and .NET 8. See the [development guide](Windows/README.md) for Windows environment requirements and the build and test commands. When reporting an issue, include your Windows version, DogSC version, steps to reproduce it, and what happened. For recording or device issues, also include the capture mode and the audio and video devices you used.

DogSC's source code is licensed under the MIT License. FFmpeg, NAudio, and .NET are distributed with the installer under their respective licenses. [MIT License](LICENSE) · [Third-party notices](Windows/THIRD-PARTY-NOTICES.txt)
