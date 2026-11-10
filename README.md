# UniLinker

**局域网设备互联平台** — 打开 App,自动发现同网络下的设备,一键投屏、传文件、远程协作。

MIT 协议,完全免费。

## 一句话

任何设备运行 UniLinker 即可互相发现、连接、协作。投屏只是第一个功能。

## 当前版本 (v3.1)

**技术栈**:C# .NET 9 + WinUI 3 + Windows.Graphics.Capture + OpenH264 + WebRTC (SIPSorcery) + Jetpack Compose

| 平台 | 状态 | 功能 |
|------|------|------|
| Windows (C#) | ✅ 可运行 | 投屏发送(浏览器观看)+ 局域网发现 + 文件传输(设备间)+ WinUI 3 GUI + 插件系统 |
| Android (Kotlin) | ⚠️ 实验 | 设备发现 + 投屏接收 + 原生 UI |
| 浏览器 | ✅ 可用 | Chrome / Edge / Firefox 直接观看(需 H264 支持) |

## 特性

- 🖥️ **低延迟投屏** — Windows.Graphics.Capture 捕获 + OpenH264 编码,浏览器一键观看
- 🔍 **局域网自动发现** — mDNS 广播 + 浏览(之前只有浏览,永远发现不了任何设备)
- 📁 **文件传输** — WebRTC DataChannel 可靠传输,SHA256 校验,拖放即发
- 🔌 **插件系统** — SDK 接口,投屏、文件传输作为独立插件(内置插件直接注册,plugins/ 目录支持外部 DLL)
- 🎨 **Fluent Design** — WinUI 3 原生设计,深浅主题、中英双语、系统托盘

## 运行

**Windows (需要 Visual Studio 2022 with Windows App SDK workload + .NET 9 SDK):**

```bash
# 构建(需要 MSBuild,dotnet build 缺少 AppxPackage 任务会失败)
"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/amd64/MSBuild.exe" UniLinker.sln -p:Configuration=Debug

# 运行
./src/UniLinker.WinUI/bin/Debug/net9.0-windows10.0.22621.0/win-x64/UniLinker.WinUI.exe
```

**首次运行须知:**

1. **Windows 防火墙**会弹窗询问 —— 点"允许",否则局域网内其他设备无法连接(本机浏览器观看不受影响)。
2. 观看投屏:任意设备浏览器打开 `http://<本机IP>:9527`,点 Connect。
3. 端口默认 9527,可在设置中修改(保存后自动重启信令服务器)。

**Android:** 用 Android Studio 打开 `android/` 目录,Sync → Run(实验性)。

## 架构

```
┌──────────────────────────────────────────────┐
│                 UniLinker                     │
│                                              │
│   Plugin SDK (接口)                           │
│   ├── ScreenMirror (投屏)  — 内置,静态注册     │
│   └── FileTransfer (文件传输) — 内置,静态注册  │
│                                              │
│   Core Platform                              │
│   ├── SignalingServer (TcpListener HTTP,     │
│   │    绑定所有网卡,9527 端口,无需管理员)       │
│   ├── PeerMesh (WebRTC 连接网格 + DataChannel)│
│   ├── Discovery (mDNS 广播 + 浏览)            │
│   └── PluginHost (内置 + plugins/ 目录加载)   │
│                                              │
│   GUI Shell (WinUI 3 / Compose)              │
└──────────────────────────────────────────────┘
```

**投屏数据流**:WGC 采集(BGRA)→ OpenH264 编码(完整 Annex-B 访问单元,SPS/PPS 随 IDR)→ SIPSorcery RTP(FU-A 分片)→ 浏览器 H264 解码。

**文件传输流**:发送方创建 DataChannel → 对方信令服务器应答 → 对方 `DataChannelReady` → FileReceiver 自动接收(Downloads/UniLinker)。

## 项目结构

```
uniLinker/
├── src/
│   ├── UniLinker.Plugin.Sdk/        # 插件 SDK(纯接口)
│   ├── UniLinker.Core/              # 平台引擎(信令/PeerMesh/发现/插件宿主)
│   ├── UniLinker.Plugins.ScreenMirror/  # 投屏插件(WGC + OpenH264)
│   ├── UniLinker.Plugins.FileTransfer/  # 文件传输插件
│   └── UniLinker.WinUI/             # Windows 主程序 (WinUI 3)
├── android/                         # Android App(实验性)
├── web/                             # 浏览器观看端(由信令服务器托管)
└── tests/UniLinker.E2E.Tests/       # Playwright E2E 测试
```

## 测试

```bash
# E2E 测试(自动启动应用;浏览器优先用本机 Edge/Chrome,因为
# Playwright 自带的 Chromium 是开源构建、不含 H264)
dotnet test tests/UniLinker.E2E.Tests --filter "Category=ScreenMirror"
```

`SM05_Browser_ReceivesActualVideoData` 是最强断言:验证 采集→编码→RTP→解码 全链路真实出帧。

## 开发要求

**Windows 开发:**
- Visual Studio 2022 with "Windows App SDK" workload
- .NET 9 SDK
- Windows 10 1903+ 或 Windows 11(TFM 固定在 10.0.22621 以匹配 Windows App SDK 1.6 投影)

**注意:** 目标框架为 `net9.0-windows10.0.22621.0`。请勿升到 26100+,否则 XamlTypeInfo 生成代码会引用不存在的 `Microsoft.Windows.UI.Xaml` 版本导致启动崩溃;插件项目必须保持相同的 `WindowsSdkPackageVersion`(WinRT.Runtime 版本必须与宿主一致)。
