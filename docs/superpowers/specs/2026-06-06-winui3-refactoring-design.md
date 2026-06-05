# WinUI3 Windows Client Refactoring Design

## Overview

Refactor the Windows client from Avalonia UI to WinUI 3 (Windows App SDK) to achieve:
- Native Windows 11 experience with Fluent Design 2.0
- Better performance (compiled bindings, native rendering)
- Deeper Windows API integration (notifications, system theme, taskbar)

## Goals

1. **Native Windows Experience** - WinUI3 is Microsoft's official modern Windows UI framework
2. **Performance Optimization** - Better rendering performance and lower memory footprint
3. **Windows API Integration** - System notifications, taskbar, theme sync, WinRT APIs

## Scope

### In Scope
- Replace `UniLinker.UI` (Avalonia) with `UniLinker.WinUI` (WinUI3)
- Replace `UniLinker.App` entry point
- Migrate all ViewModels with minimal changes
- Implement Fluent Design 2.0 UI
- Integrate with existing `UniLinker.Core` and `UniLinker.Plugin.Sdk`

### Out of Scope
- Changes to `UniLinker.Core`
- Changes to `UniLinker.Plugin.Sdk`
- Changes to `UniLinker.Plugins.ScreenMirror`
- Android client changes
- Web client changes

## Architecture

### Project Structure

```
uniLinker/
├── src/
│   ├── UniLinker.Plugin.Sdk/          # UNCHANGED
│   ├── UniLinker.Core/                # UNCHANGED
│   ├── UniLinker.Plugins.ScreenMirror/# UNCHANGED
│   │
│   ├── UniLinker.WinUI/               # NEW: WinUI3 main application
│   │   ├── App.xaml                   # Application entry point
│   │   ├── App.xaml.cs
│   │   ├── MainWindow.xaml            # Main window with NavigationView
│   │   ├── MainWindow.xaml.cs
│   │   ├── Views/
│   │   │   ├── DashboardPage.xaml
│   │   │   ├── DevicesPage.xaml
│   │   │   ├── SharePage.xaml
│   │   │   └── SettingsPage.xaml
│   │   ├── ViewModels/                # Migrated from Avalonia, adapted
│   │   │   ├── MainViewModel.cs
│   │   │   ├── DashboardViewModel.cs
│   │   │   ├── DevicesViewModel.cs
│   │   │   ├── ShareViewModel.cs
│   │   │   └── SettingsViewModel.cs
│   │   ├── Controls/
│   │   │   ├── SignalStrengthControl.xaml
│   │   │   └── ToastNotification.xaml
│   │   ├── Services/
│   │   │   ├── ToastService.cs
│   │   │   └── NavigationService.cs
│   │   ├── Styles/
│   │   │   └── AppStyles.xaml
│   │   ├── Assets/
│   │   │   └── Icons/
│   │   └── UniLinker.WinUI.csproj
│   │
│   └── UniLinker.UI/                  # DELETE after migration
│
├── UniLinker.sln                      # UPDATE solution file
└── ...
```

### Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.WindowsAppSDK` | 1.6+ | WinUI3 core framework |
| `Microsoft.Windows.SDK.BuildTools` | latest | Windows API metadata |
| `CommunityToolkit.Mvvm` | 8.4.0 | MVVM toolkit (keep existing) |
| `CommunityToolkit.WinUI.Controls` | latest | Extended controls (optional) |
| `H.NotifyIcon.Wpf` | latest | System tray icon |

## UI Design

### MainWindow Layout

```
+-------------------------------------------------------------+
|  +-- TitleBar ---------------------------------------------+  |
|  |  UNILINKER          [Search]         Bell  Gear  - X   |  |
|  +---------------------------------------------------------+  |
|                                                               |
|  +-- NavigationView (Fluent left nav) ---------------------+  |
|  |                                                          |  |
|  |   Home Dashboard    +-- Content Frame ----------------+ |  |
|  |   Devices           |                                 | |  |
|  |   Share             |   [Current page content]        | |  |
|  |   Settings          |                                 | |  |
|  |                      |                                 | |  |
|  |   ----------------   |                                 | |  |
|  |   Green Server OK    |                                 | |  |
|  |   0.0.0.0:9527       +---------------------------------+ |  |
|  |                                                          |  |
|  +----------------------------------------------------------+  |
|                                                               |
+-------------------------------------------------------------+
```

### Fluent Design Features

| Feature | Location | Description |
|---------|----------|-------------|
| Mica Background | MainWindow | Windows 11 system-level translucent background |
| Acrylic | Nav footer | Subtle transparency in status bar area |
| Rounded Corners | Cards, buttons, inputs | 8px radius, Fluent standard |
| Reveal Effect | Nav items hover | Light highlight on hover |
| System Animations | Page transitions | Built-in transition animations |

### Page Designs

#### Dashboard Page

- Status card with `InfoBar` component
- Quick stats grid (3 columns): Discovered devices, Active connections, Share sessions
- Recent devices `ListView` with swipe actions

#### Devices Page

- Discovery mode `RadioButtons`: Auto-discover / LAN / Manual IP
- Device grid `GridView` with device cards showing: Icon, Name, IP, Status, Connect button
- Manual connection `Expander` with IP input and port

#### Share Page

- Screen preview card with thumbnail
- Start/Stop share buttons
- Advanced settings `Expander`: Resolution, FPS, Bitrate
- Viewers `ListView`

#### Settings Page

- General settings `Expander`: Device name, Port, Auto-start
- Appearance `Expander`: Theme selection (Light/Dark/System)
- Plugins `Expander`: Plugin list with enable/disable

## System Integration

| Feature | Implementation | Description |
|---------|----------------|-------------|
| System Theme | `RequestedTheme` auto-sync | WinUI3 auto-responds to system theme changes |
| Mica Background | `Background = MicaBrush` | Windows 11 exclusive translucent effect |
| Notifications | `Windows.UI.Notifications` | Share start/end, device connection notifications |
| Taskbar Icon | `TaskBarIcon` (community lib) | Minimize to tray, background running |
| App Lifecycle | `AppInstance` | Single instance, prevent multiple launches |
| Clipboard | `Clipboard` API | Quick share connection code/address |

## Performance Optimization

| Optimization | Implementation |
|--------------|----------------|
| UI Virtualization | `ListView`/`GridView` built-in virtualization |
| Async Binding | `x:Bind` with `Mode=OneWay` + async property change |
| Resource Reuse | Styles/templates in `ResourceDictionary` |
| Lazy Loading | `x:Load` attribute for deferred initialization |
| Compiled Binding | `x:Bind` instead of `{Binding}` for compile-time checks |

## Core Integration

```csharp
// App.xaml.cs - Application startup
public App()
{
    InitializeComponent();

    // Initialize Core layer
    var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
    var configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "uniLinker", "config.json");

    _platform = new Platform(pluginsDir, configPath);
    _bridge = new WebBridge(_platform);
    _signalingServer = new SignalingServer(9527, _platform.PeerMesh!);

    // Register global services
    Services = new AppServices(_platform, _bridge, _signalingServer);
}

// MainWindow.xaml.cs - Inject ViewModel
public MainWindow()
{
    InitializeComponent();
    DataContext = new MainViewModel(App.Services.Bridge);
}
```

## Migration Details

### Syntax Differences

**Avalonia (AXAML):**
```xml
<ListBox ItemsSource="{Binding Devices}"
         SelectedItem="{Binding SelectedDevice}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding Name}"/>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

**WinUI3 (XAML):**
```xml
<ListView ItemsSource="{x:Bind ViewModel.Devices}"
          SelectedItem="{x:Bind ViewModel.SelectedDevice, Mode=TwoWay}">
    <ListView.ItemTemplate>
        <DataTemplate x:DataType="model:PeerInfo">
            <TextBlock Text="{x:Bind Name}"/>
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>
```

**Key Differences:**
- Use `x:Bind` instead of `{Binding}` for compiled bindings
- Declare `x:DataType` for type-safe bindings
- Control names differ: `ListBox` -> `ListView`

### Files to Migrate

| Source (Avalonia) | Target (WinUI3) | Change Level |
|-------------------|-----------------|--------------|
| `Views/MainWindow.axaml` | `MainWindow.xaml` | Rewrite |
| `Views/Pages/*.axaml` | `Views/*.xaml` | Rewrite |
| `ViewModels/*.cs` | `ViewModels/*.cs` | Minor (binding notifications) |
| `Controls/*.axaml` | `Controls/*.xaml` | Rewrite |
| `Services/ToastService.cs` | `Services/ToastService.cs` | Medium (WinUI3 notification API) |
| `WebBridge.cs` | `WebBridge.cs` | No change |

## Migration Phases

| Phase | Content | Estimated Effort |
|-------|---------|------------------|
| Phase 1 | Project setup + MainWindow + Navigation framework | 1-2 days |
| Phase 2 | Dashboard + Devices pages | 2-3 days |
| Phase 3 | Share page + Core integration testing | 1-2 days |
| Phase 4 | Settings page + System integration | 1-2 days |
| Phase 5 | Cleanup old code + Documentation update | 0.5 day |

## Success Criteria

1. All existing functionality preserved
2. Fluent Design 2.0 visual identity
3. System theme sync (light/dark)
4. System notifications working
5. Performance equal or better than Avalonia version
6. Single instance enforcement
7. Clean removal of Avalonia dependencies
