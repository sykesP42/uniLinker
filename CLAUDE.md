# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

**Windows (WinUI 3):**
```bash
# Build with MSBuild (requires Visual Studio 2022 with Windows App SDK)
"C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/amd64/MSBuild.exe" UniLinker.sln -p:Configuration=Debug

# Or with dotnet CLI (may have issues with Windows App SDK)
dotnet build src/UniLinker.WinUI/UniLinker.WinUI.csproj

# Run
./src/UniLinker.WinUI/bin/Debug/net9.0-windows10.0.19041.0/win-x64/UniLinker.WinUI.exe
```

**Android:**
```bash
# Open in Android Studio and run, or:
cd android && ./gradlew assembleDebug
```

**E2E Tests:**
```bash
# Install Playwright browsers (first time)
pwsh tests/UniLinker.E2E.Tests/bin/Debug/net9.0/playwright.ps1 install chromium

# Run tests
dotnet test tests/UniLinker.E2E.Tests

# Run specific category
dotnet test tests/UniLinker.E2E.Tests --filter "Category=ScreenMirror"
```

## Architecture

```
Plugin SDK (interfaces) → Core Platform → Plugins → UI Shell
```

| Layer | Windows (C#) | Android (Kotlin) |
|-------|-------------|-------------------|
| SDK | `UniLinker.Plugin.Sdk/` | `sdk/` |
| Core | Platform + PeerMesh + SignalingServer | Platform + WebRTCService |
| Plugins | ScreenMirror, FileTransfer | ScreenMirror |
| UI | WinUI 3 (WebView2) | Jetpack Compose |

**Key Components:**
- `Platform` — Plugin host, orchestrates lifecycle
- `PeerMesh` — WebRTC connection management, SDP exchange
- `SignalingServer` — HTTP server on port 9527 for WebRTC signaling
- `IPlugin` — Plugin interface: Initialize, OnPeerRequest, Shutdown

**Plugin System:**
- Plugins implement `IPlugin` with capabilities (e.g., "screen-capture")
- `PluginHost` loads DLLs from `plugins/` directory
- Plugins access `IPluginContext` for peers, discovery, config, UI

**Connection Flow:**
1. Browser/Android sends SDP offer to `POST /signaling`
2. `SignalingServer` creates `PeerConnection`, sets remote description
3. `PeerMesh.RaiseChannelRequestedAsync` triggers plugin's `OnPeerRequest`
4. Plugin creates channel, starts streaming

## Critical Implementation Notes

**MfEncoder COM Threading:**
- Media Foundation objects require MTA thread
- WinUI3 main thread is STA
- Solution: Dedicated MTA thread with `BlockingCollection<Action>` queue
- All MF operations (Initialize, Encode, Cleanup) must run on same thread
- See: `src/UniLinker.Plugins.ScreenMirror/MfEncoder.cs`

**Signaling Port:**
- Hardcoded to 9527 in `App.xaml.cs`
- Browser client connects to `http://localhost:9527`

## Git Conventions

- **Branch workflow:** All work on `dev/<feature>` or `fix/<bug>` branches, never commit directly to `main`
- **Commit messages:** English only, Conventional Commits format (feat/fix/chore/docs)
- **No Claude contributor:** Do not add `Co-Authored-By: Claude` in commits
