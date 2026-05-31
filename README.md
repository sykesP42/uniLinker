# UniLinker

**局域网设备互联平台** — 打开 App，自动发现同网络下的设备，一键投屏、传文件、远程协作。

MIT 协议，完全免费。

## 一句话

任何设备运行 UniLinker 即可互相发现、连接、协作。投屏只是第一个功能。

## 当前版本 (v3.0)

**技术栈**：C# .NET 9 + Windows.Graphics.Capture + Media Foundation + WebRTC + Jetpack Compose

| 平台 | 状态 | 功能 |
|------|------|------|
| Windows (C#) | ✅ 可构建 | 投屏发送 + 接收 + GUI + 插件系统 |
| Android (Kotlin) | ✅ 可构建 | 设备发现 + 投屏接收 + 原生 UI |
| 浏览器 | ✅ 兼容 | 任何支持 WebRTC 的浏览器均可观看 |

## 特性

- 🖥️ **低延迟投屏** — D3D11 零拷贝捕获 + H.264 硬件编码，端到端 4-8ms
- 📱 **全平台** — Windows 发送，Android/iOS/浏览器观看
- 🔌 **插件系统** — SDK 接口，投屏、文件传输、远程输入作为独立 Plugin
- 🔗 **多连接方式** — 局域网自动发现 / 手动 IP / 6 位连接码
- 🎨 **暗色主题** — Windows (WebView2) 和 Android (Material 3) 统一设计

## 运行

**Windows:**
```bash
dotnet run --project src/UniLinker.App
```

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
│   GUI Shell (WebView2 / Compose)              │
└──────────────────────────────────────────────┘
```

## 项目结构

```
uniLinker/
├── src/
│   ├── UniLinker.Plugin.Sdk/        # 插件 SDK（纯接口）
│   ├── UniLinker.Core/              # 平台引擎
│   ├── UniLinker.Plugins.ScreenMirror/  # 投屏插件
│   ├── UniLinker.App/               # Windows 主程序
│   └── UniLinker.UI/                # WebView2 GUI
├── android/                         # Android App
│   ├── sdk/                         # 插件 SDK (Kotlin)
│   ├── core/                        # 平台引擎
│   ├── plugins/screenmirror/        # 投屏插件
│   └── ui/                          # Compose UI
├── web/                             # 浏览器前端
└── docs/superpowers/                # 设计文档
```
