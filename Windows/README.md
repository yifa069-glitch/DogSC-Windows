# Windows 开发与测试

从仓库根目录在 PowerShell 中运行以下命令。应用面向 Windows 10 2004（内部版本 19041）及以上版本、x64 架构，编译需要 .NET 8 SDK。

## 从源码运行

录制与导出依赖 Windows 版 FFmpeg。可使用与安装包相同的 LGPL shared 构建，保留 `ffmpeg.exe`、`ffprobe.exe` 及相邻 DLL。将其目录加入 `PATH`，或在当前 PowerShell 会话中把 `DOGSC_FFMPEG_PATH` 设为解压得到的 `ffmpeg.exe` 的完整路径。所用构建与 SHA-256 见[第三方声明](THIRD-PARTY-NOTICES.txt)。

```powershell
dotnet restore .\Windows\DogSC.Windows.csproj
dotnet run --project .\Windows\DogSC.Windows.csproj --configuration Release
```

## 运行回归测试

```powershell
dotnet run --project .\Windows.SmokeTests\DogSC.Windows.SmokeTests.csproj --configuration Release --runtime win-x64
```

测试会生成临时音视频素材并调用 FFmpeg，覆盖运镜规划、时间线、项目读写和媒体导出。构建需要包括 `gdigrab`、`dshow`、`h264_mf` 和 `aac`。检查实际设备时，可分别录制窗口和选区，开启麦克风或摄像头，回放导出的 MP4 核对声画。

## 构建安装包

在 GitHub 的 **Actions → Windows build and installer → Run workflow** 运行 Windows 工作流即可生成安装包。构建脚本发布自包含的 win-x64 应用，下载并校验 FFmpeg，用 Inno Setup 6 打包，再在 Windows runner 上检查安装、启动与卸载。成功后的构建产物名为 `DogSC-Windows-Installer`。

发布版本时，项目与安装脚本中的版本号需要一致。推送同版本的 `windows-v<版本号>` 标签会在构建成功后创建带安装包的预发布版本。[构建工作流](../.github/workflows/windows-build.yml) · [安装脚本](installer.iss)
