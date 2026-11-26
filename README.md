# <img src="Windows/Assets/DogSC.png" width="40" height="40" alt=""> DogSC for Windows

简体中文 · [English](README.en.md)

DogSC 是一款在本地录制屏幕和剪辑视频的 Windows 桌面应用，适合制作软件教程。录制时会记录鼠标点击；编辑器可以按连续操作的位置自动放大画面，再平滑回到全景。

本仓库维护 Windows x64 增强版。原作由神烦老狗开发，Windows 实现基于 spring-it 的社区移植版 `windows-v0.1.21`。[macOS 原项目](https://github.com/laogou717/dogsc) · [Windows 移植基线](https://github.com/spring-it/dogsc/releases/tag/windows-v0.1.21)

## 安装

支持 Windows 10 2004（内部版本 19041）及以上版本的 64 位 Windows。下载并运行 `DogSC-Setup-win-x64.exe`。安装包内含 .NET 桌面运行时、FFmpeg 和 ffprobe，可选创建桌面快捷方式。

[下载 Windows x64 安装包](https://github.com/yifa069-glitch/DogSC-Windows/releases/download/windows-v0.1.21.11/DogSC-Setup-win-x64.exe) · [v0.1.21.11 发布说明](https://github.com/yifa069-glitch/DogSC-Windows/releases/tag/windows-v0.1.21.11) · [全部版本](https://github.com/yifa069-glitch/DogSC-Windows/releases)

## 录制并导出一段教程

1. 在「设置与帮助 → 默认输出文件」选择一个 `.mp4` 文件，也可以直接输入完整路径。首次启动会建议带时间戳的文件名；修改后，重启会恢复目录和文件名。录制时如遇同名视频或配套文件，程序会为新录屏追加 ` (2)`、` (3)` 等序号。
2. 回到「录制」，选择全屏、窗口或区域，根据需要打开系统声音、麦克风和摄像头叠加。选取窗口时应让目标内容保持可见。按 `Ctrl+Alt+M` 可在录制中添加标记。
3. 停止后点「编辑刚录制的视频」。在底部时间线上定位、分割或重排片段，切换缩略图与波形，在片段属性里调整速度和音量。点「全屏预览」可放大检查细节。
4. 点「自动运镜」调整所选片段的焦点、倍率和起止时间，或重新生成该片段的运镜。确认预览后点「开始导出」，可生成 MP4 视频或 M4A 音频。按 `Ctrl+S` 可保存项目供下次继续编辑。

若同时运行多个 DogSC 实例，自动恢复由其中一个实例负责。各实例都可以用 `Ctrl+S` 保存自己的项目。

## 自动运镜与素材

编辑器会把同一区域的一组连续点击合成一个固定构图，默认缓慢放大至 **1.3 倍**。单次孤立点击和单纯移动鼠标会保持全景。重新生成只处理选中片段的入点至出点，保留手动添加的运镜段，支持撤销。摄像头画中画已经合成在录屏中，会随画面一起缩放。

例如，`教程.mp4` 是可直接播放的全景录屏，`教程.pointer.json` 保存鼠标轨迹，录制标记保存在 `教程.markers.json`。要在其他位置重新生成运镜，请一起移动视频及轨迹文件。已有 MP4 也可导入，缺少配套轨迹时可使用手动缩放。`.dogscproj` 保存片段编辑与运镜设置，重新打开项目时需要能从保存的路径访问原素材。

## 开发与反馈

应用使用 C#、WPF 和 .NET 8。Windows 运行环境、编译及测试命令见[开发说明](Windows/README.md)。提交问题时，请附 Windows 版本、DogSC 版本、复现步骤和实际结果；录制或设备问题请注明录制模式和使用的音视频设备。

DogSC 源码采用 MIT 许可证。随安装包分发的 FFmpeg、NAudio 和 .NET 各自遵循其许可证。[MIT 许可证](LICENSE) · [第三方声明](Windows/THIRD-PARTY-NOTICES.txt)
