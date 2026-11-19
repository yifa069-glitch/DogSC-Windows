# Windows 开发与测试

从仓库根目录在 PowerShell 中运行以下命令。应用面向 Windows 10 2004（内部版本 19041）及以上版本、x64 架构，编译需要 .NET 8 SDK。

## 从源码运行

录制与导出依赖 Windows 版 FFmpeg。可使用与安装包相同的 LGPL shared 构建，保留 `ffmpeg.exe`、`ffprobe.exe` 及相邻 DLL。将其目录加入 `PATH`，或在当前 PowerShell 会话中把 `DOGSC_FFMPEG_PATH` 设为解压得到的 `ffmpeg.exe` 的完整路径。所用构建与 SHA-256 见[第三方声明](THIRD-PARTY-NOTICES.txt)。

```powershell
dotnet restore .\Windows\DogSC.Windows.csproj --runtime win-x64
dotnet run --project .\Windows\DogSC.Windows.csproj --configuration Release --runtime win-x64
```

## 运行回归测试

```powershell
dotnet run --project .\Windows.SmokeTests\DogSC.Windows.SmokeTests.csproj --configuration Release --runtime win-x64
```

测试会生成临时音视频素材并调用 FFmpeg，覆盖运镜规划、时间线、项目读写和媒体导出。用于录制和测试的 FFmpeg 需要包含 `gdigrab`、`dshow`、`h264_mf` 和 `aac`。检查实际设备时，可分别录制窗口和选区，开启麦克风或摄像头，回放导出的 MP4 核对声画。

## 构建安装包

推送 `main` 上的 Windows 源码或构建配置时，GitHub Actions 会运行 **Windows build and installer**。有仓库写入权限的维护者也可用 **Run workflow** 手动构建。工作流生成自包含的 win-x64 应用，下载并校验 FFmpeg，用 Inno Setup 6 打包，再检查安装、启动与卸载。登录 GitHub 后，可从成功的构建页面下载保留 30 天的 `DogSC-Windows-Installer`。

发布版本时，更新项目和安装脚本中的版本号。为对应提交推送 `windows-v<版本号>` 标签，构建和安装验证通过后，工作流会把安装包发布到 Releases。更新根 README 的下载链接后，使用未登录的浏览器验证附件可下载。[构建工作流](../.github/workflows/windows-build.yml) · [安装脚本](installer.iss)
