# UniLinker

**局域网设备互联平台** — 打开 App，自动发现同网络下的设备，一键投屏、传文件、远程协作。

MIT 协议，完全免费。

## 一句话

任何设备运行 UniLinker 即可互相发现、连接、协作。投屏只是第一个功能。

## 当前版本 (v3.0)

**技术栈**：C# .NET 9 + WinUI 3 + Windows.Graphics.Capture + Media Foundation + WebRTC + Jetpack Compose

| 平台 | 状态 | 功能 |
|------|------|------|
| Windows (C#) | ✅ 可运行 | 投屏发送 + 接收 + WinUI3 GUI + 插件系统 |
| Android (Kotlin) | ✅ 可构建 | 设备发现 + 投屏接收 + 原生 UI |
| 浏览器 | ✅ 兼容 | 任何支持 WebRTC 的浏览器均可观看 |

## 特性

- 🖥️ **低延迟投屏** — D3D11 零拷贝捕获 + H.264 硬件编码，端到端 4-8ms
- 📱 **全平台** — Windows 发送，Android/iOS/浏览器观看
- 🔌 **插件系统** — SDK 接口，投屏、文件传输、远程输入作为独立 Plugin
- 🔗 **多连接方式** — 局域网自动发现 / 手动 IP / 6 位连接码
- 🎨 **Fluent Design** — Windows (WinUI 3) 和 Android (Material 3) 原生设计

## 运行

**Windows (需要 Visual Studio 2022 with Windows App SDK):**
```bash
# 使用 MSBuild 构建
"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/amd64/MSBuild.exe" UniLinker.sln -p:Configuration=Debug

# 运行
./src/UniLinker.WinUI/bin/Debug/net9.0-windows10.0.19041.0/UniLinker.WinUI.exe
```

**或使用 Visual Studio:**
1. 打开 `UniLinker.sln`
2. 右键 `UniLinker.WinUI` → 设为启动项目
3. 按 F5 运行

**Android:**
用 Android Studio 打开 `android/` 目录，Sync → Run

## 架构

```
┌──────────────────────────────────────────────┐
│                 UniLinker                     │
│                                              │
│   Plugin SDK (接口)                           │
│   ├── ScreenMirror (投屏)                     │
│   ├── FileTransfer (文件传输) — 未来           │
│   └── RemoteInput (远程输入) — 未来           │
│                                              │
│   Core Platform                              │
│   ├── Peer Mesh (WebRTC 连接网格)             │
│   ├── Discovery (mDNS 设备发现)               │
│   ├── Strategies (局域网/手动IP/连接码)        │
│   └── Signaling (HTTP 信令)                   │
│                                              │
│   GUI Shell (WinUI 3 / Compose)               │
└──────────────────────────────────────────────┘
```

## 项目结构

```
uniLinker/
├── src/
│   ├── UniLinker.Plugin.Sdk/        # 插件 SDK（纯接口）
│   ├── UniLinker.Core/              # 平台引擎
│   ├── UniLinker.Plugins.ScreenMirror/  # 投屏插件
│   └── UniLinker.WinUI/             # Windows 主程序 (WinUI 3)
├── android/                         # Android App
│   ├── sdk/                         # 插件 SDK (Kotlin)
│   ├── core/                        # 平台引擎
│   ├── plugins/screenmirror/        # 投屏插件
│   └── ui/                          # Compose UI
├── web/                             # 浏览器前端
└── docs/superpowers/                # 设计文档
```

## 开发要求

**Windows 开发:**
- Visual Studio 2022 with "Windows App SDK" workload
- .NET 9 SDK
- Windows 10 1809+ 或 Windows 11

**Android 开发:**
- Android Studio Hedgehog+
- JDK 17+
- Android SDK 34
