# UniLinker Android App

Native Android client for UniLinker P2P screen mirroring platform.

## Features

- Auto-discover UniLinker Windows instances on LAN via mDNS
- One-tap connect to view remote screen
- H.264 hardware-accelerated decoding via WebRTC
- Real-time stats overlay (resolution, FPS, decode latency)
- Jetpack Compose Material 3 dark theme UI

## Build

1. Open `android/` in Android Studio
2. Sync Gradle
3. Run on device or emulator (min SDK 26, Android 8.0+)

## Architecture

```
MainActivity.kt (Compose UI)
  ├── DeviceDiscovery.kt (NSD/mDNS)
  │     → discovers _unilinker._tcp.local
  ├── WebRTCClient.kt (Google WebRTC)
  │     → SDP offer/answer via HTTP signaling
  │     → H.264 video track reception
  ├── DeviceListScreen.kt (Compose)
  └── StreamScreen.kt (SurfaceViewRenderer + stats)
```

## How It Works

1. Android app broadcasts mDNS query for `_unilinker._tcp.local`
2. Windows uniLinker instances respond with their IP:port
3. User taps a device → WebRTC offer sent to `http://<IP>:9527/signaling`
4. ICE candidates exchanged through `/ice` endpoint
5. H.264 video track received → SurfaceViewRenderer displays it

## Requirements

- Android 8.0+ (API 26)
- Same LAN as Windows uniLinker instance
