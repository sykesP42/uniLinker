# UniLinker Android App

Native Android client for UniLinker P2P device interconnection platform.

## Architecture (mirrors Windows C# platform)

```
MainActivity.kt (Plugin Host)
  │
  ├── sdk/                      # Plugin SDK (interfaces only)
  │   ├── IPlugin.kt            # Plugin contract
  │   ├── IPluginContext.kt     # Platform services injected to plugins
  │   ├── IPeerMesh.kt          # WebRTC peer connection management
  │   ├── IDeviceDiscovery.kt   # mDNS/NSD device discovery
  │   ├── IConfigStore.kt       # Key-value config persistence
  │   ├── IUIProvider.kt        # Dynamic tab registration
  │   └── models/Models.kt      # Shared data models
  │
  ├── core/                     # Platform services
  │   ├── Platform.kt           # Plugin host + lifecycle orchestrator
  │   ├── DiscoveryService.kt   # Android NSD (mDNS) implementation
  │   ├── WebRTCService.kt      # Google WebRTC PeerConnection wrapper
  │   └── ConfigStore.kt        # SharedPreferences-based config
  │
  ├── plugins/                  # Feature plugins
  │   └── screenmirror/         # Plugin: Screen Mirroring
  │       ├── ScreenMirrorPlugin.kt  # IPlugin implementation
  │       └── ScreenMirrorTab.kt     # Compose UI tab
  │
  └── ui/                       # UI shell
      ├── MainScreen.kt         # Tab container + navigation
      └── theme/Theme.kt        # Material 3 dark theme
```

## Plugin System

Each plugin implements `IPlugin`:

```kotlin
class MyPlugin : IPlugin {
    override val id = "com.unilinker.my-plugin"
    override val name = "My Plugin"
    override val capabilities = listOf("my-capability")

    override suspend fun initialize(context: IPluginContext): Boolean {
        // Register UI tab
        context.ui.registerTab(PluginTab(id, name, icon) { MyTabContent() })
        // Access device discovery
        context.discovery.discover().collect { devices -> ... }
        // Store settings
        context.config.set("key", "value")
        return true
    }
}
```

Plugins are registered in MainActivity:

```kotlin
platform.registerPlugin(ScreenMirrorPlugin())
platform.registerPlugin(FileTransferPlugin())  // future
platform.registerPlugin(ClipboardPlugin())      // future
```

## Future Plugins

| Plugin | ID | Capabilities |
|--------|------|-------------|
| Screen Mirror | `com.unilinker.screen-mirror` | screen-capture, video-stream |
| File Transfer | `com.unilinker.file-transfer` | file-system, data-channel |
| Clipboard Sync | `com.unilinker.clipboard` | clipboard, data-channel |
| Remote Input | `com.unilinker.remote-input` | input-simulation |

## Build

1. Open `android/` in Android Studio
2. Sync Gradle
3. Run on device (min SDK 26, Android 8.0+)

## Requirements

- Android 8.0+ (API 26)
- Same LAN as Windows uniLinker instance
- Windows uniLinker running with standard WebRTC signaling on port 9527
