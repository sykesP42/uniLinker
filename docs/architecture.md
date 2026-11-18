# 架构与实现坑位（从 CLAUDE.md 迁入，2026-11-16）

本文件是技术细节的唯一真相源；行为约束见根目录 AGENTS.md。

## Build

**Windows (WinUI 3)：**

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
- `SignalingService` (Core) — transport-free signaling business logic (offer handling, loopback candidate filter, info/status payloads); wire DTOs `SignalingMessage`/`IceCandidateMessage` live here, shapes locked by `SignalingContractTests`
- `SignalingKestrelHost` (WinUI) — **Kestrel on 0.0.0.0:9527 (IPv6 dual-mode)**, pure transport mapping onto `SignalingService` (replaced the hand-rolled TcpListener server in M2). Endpoints: `/info`, `/api/status`, `/signaling`, `/ice`, static viewer from `web/`; CORS `*` on every response; OPTIONS → 204. `/ice-pending` was removed as dead code
- `DiscoveryService` — mDNS advertise (Makaretu.Dns.Multicast) + browse (Zeroconf)
- `IPlugin` / `IIncomingChannelHandler` — plugin interfaces; the latter receives data channels opened by remote peers

**Connection Flow:**

1. Browser sends SDP offer to `POST /signaling`
2. `SignalingServer` creates a `PeerConnection`, answers, and registers **that same instance** in `PeerMesh`
3. `PeerMesh.RaiseChannelRequestedAsync` triggers the plugin's `OnChannelRequested`
4. Plugin starts capture/encoding; `PeerChannel.SendPacketAsync` → `RTCPeerConnection.SendVideoFrame(duration, annexBFrame)` (SIPSorcery parses NALs and fragments FU-A itself)

**Screen mirror pipeline** (`ScreenMirrorPlugin`): WGC capture (BGRA, staging readback, tier-scaled pool) → encoder selection `screen-mirror:encoder = auto | nvenc | openh264` (**auto** tries NVENC first, falls back to CPU on open failure) → all open viewer channels. One shared capture session; IDR forced when a viewer joins (`IEncoder.ForceKeyFrame`, part of the SDK contract).

- `OpenH264Encoder` — H264Sharp CPU encode; one complete Annex-B access unit per frame; re-encodes the last frame when the desktop is static; SemaphoreSlim(0,128) latest-wins slot (capacity overflow at 8ms capture cadence was a real crash).
- `NvEncEncoder` — FFmpeg.AutoGen `h264_nvenc` (GPU, near-zero CPU): sws_scale BGRA→YUV420P, `pict_type=I` on ForceKeyFrame, preset p1 + tune ull + CBR + delay 0. `Initialize()` opens the codec synchronously so `auto` can fall back. FFmpeg shared DLLs (avcodec-63 etc.) copy from `third_party/ffmpeg/` at build time — h264_nvenc P/Invokes resolve against the exe dir.

**Direct-RTP tier (M3/M4, the primary Win↔Android path):** the Android app sends `type=rtp` (its UDP port + requested CaptureWidth/Height/BitrateKbps/CaptureFps tier) over the HTTPS 9528 pairing channel → `SignalingService.HandleRtpSessionAsync` builds a `DirectRtpChannel` (SIPSorcery RTPSession, SDES keying, crypto line **taken from the sender's announced offer** — externally injected keys do not protect outbound) and pre-registers it in PeerMesh. Sender: SRTP-protected Annex-B over UDP. Receiver (Android): `DirectRtpReceiver` reorders by seq-gap BEFORE unprotect, jitsi-srtp unprotect (needs `SunJceAlias.register()`), RFC 6184 FU-A/STAP-A reassembly, marker-bit frame boundary, `H264SurfaceDecoder` (MediaCodec→Surface). Diagnostics: 5s sliding-window loss (seq-gap) + RFC-3550 jitter shown on the on-device HUD. Chaos acceptance: docs/spec/chaos-acceptance.md.

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

**NVENC encoder (NvEncEncoder.cs):**

- Raw `NV_ENC_FUNCTIONS` binding fails with 0xF (SDK struct version mismatch) — **FFmpeg.AutoGen is the only viable binding**; `avcodec_find_encoder_by_name("h264_nvenc")` + `avcodec_open2` + sws_scale + send/receive works (probed on RTX 5070 Ti, driver 610.x)
- FFmpeg shared DLLs resolve against the **exe dir**, not the plugin dir — the csproj `<None CopyToOutputDirectory>` item flows them from `third_party/ffmpeg/` into the host output via the ProjectReference
- `Initialize()` must open the codec **synchronously** (not lazily on first frame) — `encoder=auto` falls back to OpenH264 only if it can fail early; WGC frames may be `0`-configured tiers, pass `WgcCapture.OutputWidth/Height` (actual per-frame dims), never the config tier
- FFmpeg.AutoGen 9 API: sws flags live in the `SwsFlags` enum, picture types in `AVPictureType`; `avcodec_free_context`/`av_frame_free`/`av_packet_free` take `&local` — `&classField` is CS0212 without pinning

**Signaling port:** default 9527, configurable in Settings (saving restarts the server at runtime). First run triggers a Windows Firewall prompt — required for LAN access.

**Kestrel host gotchas (M2, all empirical):**
- Web root must be set via `WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = ... })` — `builder.WebHost.UseWebRoot()` afterwards throws `NotSupportedException`
- OPTIONS preflight must short-circuit in MIDDLEWARE; a catch-all `MapMethods("{*path}", "OPTIONS")` route candidate makes ASP.NET answer **405** for plain GETs
- OnLaunched server failures must go to the file log (`Log.Info`) — `Debug.WriteLine` is invisible in an unpackaged GUI app
- Adding/refreshing FrameworkReference requires `msbuild -restore`, or runtimeconfig misses Microsoft.AspNetCore.App and startup FileNotFound-crashes (in AGENTS.md)

**Direct RTP tier (M3, `DirectRtpChannel` + Android `mediatier/`):**
- Windows sender: SIPSorcery `RTPSession` in `RtpSecureMediaOption.SdpCryptoNegotiation` mode; **CryptoLine is extracted from the generated offer** — SIPSorcery protects outbound with the key it announces there; an externally injected key does NOT reach the protection path (interop-diagnosed by `SenderOutput_unprotects_with_own_crypto_line`)
- Fabricated answer echoes our own crypto line (one-way stream, harmless key reuse); `SetDestination` = fixed UDP endpoint, no ICE
- Android receiver: jitsi-srtp (`org.jitsi:jitsi-srtp`) per-SSRC unprotect + RFC 6184 FU-A/STAP-A depacketizer (`H264RtpDepacketizer`) + MediaCodec→Surface (`H264SurfaceDecoder`); entry = `DirectRtpActivity` (deep link `--es connect "ip:9527"`)
- Cross-language conformance: `tests/UniLinker.Core.Tests/Fixtures/direct-rtp-fixture.json` regenerated via `UNILINKER_WRITE_FIXTURE=1`, consumed by Android JVM tests (`DirectRtpFixtureTest`)
- Control plane: `POST /signaling {type:"rtp", rtpPort}` → `{type:"rtp-answer", crypto, ssrc, rtpPort}`; plugin viewer flow unchanged via `PeerMesh.RegisterDirectChannel`
- SRTP payload bytes are ciphertext — the only reliable on-the-wire signal that protection is active is the +10-byte auth tag per packet
- Gradle on this machine: bypass `gradlew` under proxy (PKIX); call the unpacked dist directly — `%USERPROFILE%\.gradle\wrapper\dists\gradle-8.9-bin\78qddjpeqn5v6yec3xb8kv9ca\gradle-8.9\bin\gradle` with `JAVA_HOME` = Android Studio JBR

**Diagnostics:** file logs at `%APPDATA%\uniLinker\unilinker.log` (plugin + server) and `crash.log` (unhandled exceptions). Console output is invisible for unpackaged WinUI apps.
