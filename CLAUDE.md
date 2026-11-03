# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

**Windows (WinUI 3):**
```bash
# Build with MSBuild (requires Visual Studio 2022 with Windows App SDK workload).
# dotnet build fails: it lacks the AppxPackage PRI tasks used by WindowsAppSDK.
"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/amd64/MSBuild.exe" UniLinker.sln -p:Configuration=Debug

# Run
./src/UniLinker.WinUI/bin/Debug/net9.0-windows10.0.22621.0/win-x64/UniLinker.WinUI.exe
```

**E2E Tests** (self-contained: the fixture launches the real app):
```bash
dotnet test tests/UniLinker.E2E.Tests --filter "Category=ScreenMirror"
# Debug-only diagnostics (SDP dumps, screenshots):
dotnet test tests/UniLinker.E2E.Tests --filter "Category=Debug"
```
The Playwright fixture prefers the installed Edge/Chrome channel — the bundled
Chromium build has **no H264 codec**, so screen mirror tests fail on it.

## Architecture

```
Plugin SDK (interfaces) → Core Platform → Plugins → UI Shell
```

| Layer | Windows (C#) | Android (Kotlin) |
|-------|-------------|-------------------|
| SDK | `UniLinker.Plugin.Sdk/` | `sdk/` |
| Core | Platform + PeerMesh + SignalingServer | Platform + WebRTCService |
| Plugins | ScreenMirror, FileTransfer (built-in) | ScreenMirror |
| UI | WinUI 3 | Jetpack Compose |

**Key Components:**
- `Platform` — plugin host, orchestrates lifecycle; built-in plugins register via `Platform.RegisterPlugin()`, external DLLs are still loaded from `<output>/plugins/`
- `PeerMesh` — WebRTC connection management; `RegisterIncomingConnection(peer, pc)` binds the exact negotiated connection
- `SignalingServer` — **TcpListener-based HTTP server on 0.0.0.0:9527** (HttpListener was localhost-only and http.sys needs admin URL ACLs). Endpoints: `/info`, `/api/status`, `/signaling`, `/ice`, `/ice-pending`, static files from `web/`
- `DiscoveryService` — mDNS advertise (Makaretu.Dns.Multicast) + browse (Zeroconf)
- `IPlugin` / `IIncomingChannelHandler` — plugin interfaces; the latter receives data channels opened by remote peers

**Connection Flow:**
1. Browser sends SDP offer to `POST /signaling`
2. `SignalingServer` creates a `PeerConnection`, answers, and registers **that same instance** in `PeerMesh`
3. `PeerMesh.RaiseChannelRequestedAsync` triggers the plugin's `OnChannelRequested`
4. Plugin starts capture/encoding; `PeerChannel.SendPacketAsync` → `RTCPeerConnection.SendVideoFrame(duration, annexBFrame)` (SIPSorcery parses NALs and fragments FU-A itself)

**Screen mirror pipeline** (`ScreenMirrorPlugin`): WGC capture (BGRA, staging readback) → `OpenH264Encoder` (H264Sharp; emits one complete Annex-B access unit per frame, re-encodes the last frame when the desktop is static) → all open viewer channels. One shared capture session; IDR forced when a viewer joins.

## Critical Implementation Notes

**Target framework pinning:**
- All WinUI/plugin projects use `net9.0-windows10.0.22621.0` + `WindowsSdkPackageVersion 10.0.22621.38` to match Windows App SDK 1.6's projection. Raising the TFM to 26100 makes XamlTypeInfo reference a non-existent `Microsoft.Windows.UI.Xaml` version → instant startup crash. Plugin and host **must** share the same WinRT.Runtime version or plugin assembly load fails.

**WGC capture (WgcCapture.cs):**
- `IGraphicsCaptureItemInterop.CreateForMonitor` (HRESULT, 3 args) — the DisplayId API is absent from the pinned projection
- Surface access MUST use `WinRT.CastExtensions.As<T>()`; a raw COM cast throws on CsWinRT 2.x
- `Direct3D11CaptureFramePool.CreateFreeThreaded` (no DispatcherQueue on plugin threads)
- `_isCapturing` must be set **before** `StartCapture` — on a static desktop the only delivered frame races the flag otherwise

**Encoder (OpenH264Encoder.cs):**
- `MFCreateMemoryByteStream` does not exist as an export — the old Media Foundation SinkWriter encoder could never initialize; do not resurrect it
- H264Sharp's `RgbImage(Bgra, w, h, stride, data)` handles stride-padded WGC frames natively

**Signaling port:** default 9527, configurable in Settings (saving restarts the server at runtime). First run triggers a Windows Firewall prompt — required for LAN access.

**Diagnostics:** file logs at `%APPDATA%\uniLinker\unilinker.log` (plugin + server) and `crash.log` (unhandled exceptions). Console output is invisible for unpackaged WinUI apps.

## Git Conventions

- **Branch workflow:** All work on `dev/<feature>` or `fix/<bug>` branches, never commit directly to `main`
- **Commit messages:** English only, Conventional Commits format (feat/fix/chore/docs)
- **No Claude contributor:** Do not add `Co-Authored-By: Claude` in commits
