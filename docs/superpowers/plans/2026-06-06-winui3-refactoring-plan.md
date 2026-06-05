# WinUI3 Windows Client Refactoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Avalonia UI with WinUI3 to achieve native Windows 11 experience with Fluent Design 2.0.

**Architecture:** Create new `UniLinker.WinUI` project with WinUI3, migrate ViewModels with minimal changes, keep Core/Plugin layers untouched. Use NavigationView for shell, compiled bindings (`x:Bind`) for performance.

**Tech Stack:** WinUI 3 (Windows App SDK 1.6+), .NET 9, CommunityToolkit.Mvvm 8.4.0

---

## File Structure

### New Files to Create

```
src/UniLinker.WinUI/
├── App.xaml                          # Application entry, resources
├── App.xaml.cs                       # Startup logic, service registration
├── MainWindow.xaml                   # Main window with NavigationView
├── MainWindow.xaml.cs                # Window code-behind
├── UniLinker.WinUI.csproj            # Project file
├── Views/
│   ├── DashboardPage.xaml            # Dashboard UI
│   ├── DashboardPage.xaml.cs
│   ├── DevicesPage.xaml              # Device discovery UI
│   ├── DevicesPage.xaml.cs
│   ├── SharePage.xaml                # Screen sharing UI
│   ├── SharePage.xaml.cs
│   ├── SettingsPage.xaml             # Settings UI
│   └── SettingsPage.xaml.cs
├── ViewModels/
│   ├── MainViewModel.cs              # Navigation + app state
│   ├── DashboardViewModel.cs         # Dashboard logic
│   ├── DevicesViewModel.cs           # Device discovery logic
│   ├── ShareViewModel.cs             # Share control logic
│   └── SettingsViewModel.cs          # Settings logic
├── Controls/
│   ├── SignalStrengthControl.xaml    # Signal indicator
│   ├── SignalStrengthControl.xaml.cs
│   ├── DeviceCard.xaml               # Device card template
│   └── DeviceCard.xaml.cs
├── Services/
│   ├── AppServices.cs                # DI container for services
│   └── NavigationService.cs          # Page navigation helper
├── Models/
│   └── ConnectionQuality.cs          # Enums and models
├── Styles/
│   └── AppStyles.xaml                # Custom styles
└── Assets/
    └── Icons/                        # App icons
```

### Files to Modify

```
UniLinker.sln                         # Add new project reference
```

### Files to Delete (after migration complete)

```
src/UniLinker.App/                    # Old entry point
src/UniLinker.UI/                     # Old Avalonia UI
```

---

## Phase 1: Project Setup and Shell

### Task 1: Create WinUI3 Project

**Files:**
- Create: `src/UniLinker.WinUI/UniLinker.WinUI.csproj`

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <RootNamespace>UniLinker.WinUI</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Platforms>x86;x64;ARM64</Platforms>
    <RuntimeIdentifiers>win-x86;win-x64;win-arm64</RuntimeIdentifiers>
    <UseWinUI>true</UseWinUI>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.6.250114005" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.1742" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\UniLinker.Core\UniLinker.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create app.manifest**

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="UniLinker.WinUI.app"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
```

- [ ] **Step 3: Update solution file**

Add the new project to `UniLinker.sln`:

```bash
dotnet sln add src/UniLinker.WinUI/UniLinker.WinUI.csproj
```

- [ ] **Step 4: Commit**

```bash
git add src/UniLinker.WinUI/UniLinker.WinUI.csproj src/UniLinker.WinUI/app.manifest UniLinker.sln
git commit -m "feat: create WinUI3 project structure"
```

---

### Task 2: Create App Entry Point

**Files:**
- Create: `src/UniLinker.WinUI/App.xaml`
- Create: `src/UniLinker.WinUI/App.xaml.cs`

- [ ] **Step 1: Create App.xaml**

```xml
<Application
    x:Class="UniLinker.WinUI.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
                <ResourceDictionary Source="Styles/AppStyles.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 2: Create App.xaml.cs**

```csharp
using Microsoft.UI.Xaml;
using UniLinker.Core;
using UniLinker.WinUI.Services;
using UniLinker.WinUI.ViewModels;

namespace UniLinker.WinUI;

public partial class App : Application
{
    private Platform? _platform;
    private WebBridge? _bridge;
    private SignalingServer? _signalingServer;
    private MainWindow? _window;

    public static AppServices Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "uniLinker", "config.json");

        _platform = new Platform(pluginsDir, configPath);
        _bridge = new WebBridge(_platform);
        _signalingServer = new SignalingServer(9527, _platform.PeerMesh!);

        Services = new AppServices(_platform, _bridge, _signalingServer);

        await _platform.StartAsync();

        _window = new MainWindow();
        _window.Activate();
    }

    protected override async void OnSuspending(object sender, SuspendingEventArgs e)
    {
        var deferral = e.SuspendingOperation.GetDeferral();
        if (_platform != null)
        {
            await _platform.StopAsync();
        }
        deferral.Complete();
    }
}
```

- [ ] **Step 3: Create placeholder AppStyles.xaml**

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Custom styles will be added here -->
</ResourceDictionary>
```

- [ ] **Step 4: Commit**

```bash
git add src/UniLinker.WinUI/App.xaml src/UniLinker.WinUI/App.xaml.cs src/UniLinker.WinUI/Styles/AppStyles.xaml
git commit -m "feat: add WinUI3 app entry point with Core integration"
```

---

### Task 3: Create Services Layer

**Files:**
- Create: `src/UniLinker.WinUI/Services/AppServices.cs`
- Create: `src/UniLinker.WinUI/Services/NavigationService.cs`

- [ ] **Step 1: Create AppServices.cs**

```csharp
using UniLinker.Core;

namespace UniLinker.WinUI.Services;

public class AppServices
{
    public Platform Platform { get; }
    public WebBridge Bridge { get; }
    public SignalingServer SignalingServer { get; }
    public NavigationService Navigation { get; }

    public AppServices(Platform platform, WebBridge bridge, SignalingServer signalingServer)
    {
        Platform = platform;
        Bridge = bridge;
        SignalingServer = signalingServer;
        Navigation = new NavigationService();
    }
}
```

- [ ] **Step 2: Create NavigationService.cs**

```csharp
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace UniLinker.WinUI.Services;

public class NavigationService
{
    private Frame? _frame;

    public void Initialize(Frame frame)
    {
        _frame = frame;
    }

    public bool NavigateTo(Type pageType, object? parameter = null)
    {
        if (_frame == null) return false;

        // Don't navigate to the same page
        if (_frame.Content?.GetType() == pageType) return false;

        _frame.Navigate(pageType, parameter, new SlideNavigationTransitionInfo
        {
            Effect = SlideNavigationTransitionEffect.FromRight
        });
        return true;
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
        {
            _frame.GoBack();
        }
    }

    public void ClearHistory()
    {
        _frame?.BackStack.Clear();
    }
}
```

- [ ] **Step 3: Create WebBridge.cs (copy from UI project)**

```csharp
using System.Text.Json;
using UniLinker.Core;
using UniLinker.Plugin.Sdk;

namespace UniLinker.WinUI.Services;

public class WebBridge
{
    public Platform Platform { get; }
    private readonly Platform _platform;

    public WebBridge(Platform platform)
    {
        _platform = platform;
        Platform = platform;
    }

    public IReadOnlyList<PeerInfo> GetDiscoveredDevices()
    {
        return _platform.Discovery?.KnownDevices ?? Array.Empty<PeerInfo>().AsReadOnly();
    }

    public (string DeviceName, int Port, int Peers) GetStatusInfo()
    {
        var config = _platform.Context.Config.Get<PlatformConfig>("platform");
        return (
            config.DeviceName ?? Environment.MachineName,
            config.HttpPort > 0 ? config.HttpPort : 9527,
            _platform.PeerMesh?.ConnectedPeers.Count ?? 0
        );
    }

    public async void StartSharing(string configJson)
    {
        try
        {
            var opts = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson);
            _platform.Context.Config.Set("screen-mirror", new
            {
                CaptureWidth = opts?.GetValueOrDefault("width", default).GetInt32() ?? 1920,
                CaptureHeight = opts?.GetValueOrDefault("height", default).GetInt32() ?? 1080,
                CaptureFps = opts?.GetValueOrDefault("fps", default).GetInt32() ?? 30,
                BitrateKbps = opts?.GetValueOrDefault("bitrate", default).GetInt32() ?? 15000,
            });
            await _platform.Context.Config.SaveAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StartSharing error: {ex.Message}");
        }
    }

    public async void StopSharing()
    {
        try
        {
            if (_platform.PeerMesh != null)
            {
                var peers = _platform.PeerMesh.ConnectedPeers;
                foreach (var peer in peers)
                {
                    await _platform.PeerMesh.DisconnectPeer(peer);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StopSharing error: {ex.Message}");
        }
    }

    public async void WatchDevice(string peerId)
    {
        try
        {
            var peer = _platform.Discovery?.KnownDevices.FirstOrDefault(p => p.Id == peerId);
            if (peer == null) return;

            var channel = await (_platform.PeerMesh?.CreateChannel(peer, "screen-capture") ?? Task.FromResult<IChannel?>(null));

            if (channel != null)
            {
                channel.PacketReceived += packet =>
                {
                    System.Diagnostics.Debug.WriteLine($"[Watch] Received packet {packet.Data.Length} bytes");
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WatchDevice error: {ex.Message}");
        }
    }

    public string GetConfig()
    {
        var config = _platform.Context.Config.Get<PlatformConfig>("platform");
        return JsonSerializer.Serialize(config);
    }

    public void SaveConfig(string configJson)
    {
        try
        {
            var config = JsonSerializer.Deserialize<PlatformConfig>(configJson);
            if (config != null)
            {
                _platform.Context.Config.Set("platform", config);
                _ = _platform.Context.Config.SaveAsync();
            }
        }
        catch { }
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add src/UniLinker.WinUI/Services/
git commit -m "feat: add service layer for WinUI3 app"
```

---

### Task 4: Create Models

**Files:**
- Create: `src/UniLinker.WinUI/Models/ConnectionQuality.cs`

- [ ] **Step 1: Create ConnectionQuality.cs**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace UniLinker.WinUI.Models;

public enum ConnectionQuality
{
    Excellent = 4,
    Good = 3,
    Fair = 2,
    Poor = 1
}

public enum ErrorSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

public partial class ErrorInfo : ObservableObject
{
    public ErrorSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
    public string[] Suggestions { get; init; } = Array.Empty<string>();
    public string? ActionLabel { get; init; }
    public bool IsDismissible { get; init; } = true;
    public int AutoDismissMs { get; init; } = 0;
    public string? ErrorCode { get; init; }
}

public partial class StreamStats : ObservableObject
{
    [ObservableProperty] private int _width;
    [ObservableProperty] private int _height;
    [ObservableProperty] private int _fps;
    [ObservableProperty] private int _bitrateKbps;
    [ObservableProperty] private int _latencyMs;
    [ObservableProperty] private double _packetLossPercent;
    [ObservableProperty] private int _rttMs;
    [ObservableProperty] private ConnectionQuality _quality = ConnectionQuality.Good;

    public string Resolution => $"{Width}x{Height}";
    public string BitrateDisplay => BitrateKbps >= 1000 ? $"{BitrateKbps / 1000.0:F1} Mbps" : $"{BitrateKbps} Kbps";

    public static ConnectionQuality CalculateQuality(int rttMs, double packetLossPercent, int bitrateKbps)
    {
        int score = 4;
        if (rttMs > 100) score--;
        if (rttMs > 200) score--;
        if (packetLossPercent > 1) score--;
        if (packetLossPercent > 5) score--;
        if (bitrateKbps < 2000) score--;
        return (ConnectionQuality)Math.Max(1, Math.Min(4, score));
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/UniLinker.WinUI/Models/ConnectionQuality.cs
git commit -m "feat: add models for WinUI3 app"
```

---

### Task 5: Create MainViewModel

**Files:**
- Create: `src/UniLinker.WinUI/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Create MainViewModel.cs**

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using UniLinker.Plugin.Sdk;
using UniLinker.WinUI.Models;
using UniLinker.WinUI.Services;

namespace UniLinker.WinUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly WebBridge? _bridge;
    private readonly DispatcherQueue _dispatcherQueue;

    [ObservableProperty] private int _currentNavIndex = 0;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _deviceInfo = "Local: UniLinker";
    [ObservableProperty] private string _connectAddress = "Listening: 0.0.0.0:9527";
    [ObservableProperty] private int _deviceCount;
    [ObservableProperty] private ConnectionQuality _connectionQuality = ConnectionQuality.Good;

    public DashboardViewModel Dashboard { get; }
    public DevicesViewModel Devices { get; }
    public ShareViewModel Share { get; }
    public SettingsViewModel Settings { get; }

    public MainViewModel()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        Dashboard = new();
        Devices = new();
        Share = new();
        Settings = new();
    }

    public MainViewModel(WebBridge bridge) : this()
    {
        _bridge = bridge;
        StartRefreshTimer();
    }

    private void StartRefreshTimer()
    {
        var timer = new System.Timers.Timer(3000);
        timer.Elapsed += (s, e) => _dispatcherQueue.TryEnqueue(RefreshDevices);
        timer.Start();
        RefreshDevices();
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        Devices.Devices.Clear();

        var discovered = _bridge?.GetDiscoveredDevices();
        if (discovered == null || discovered.Count == 0)
        {
            StatusText = "Searching...";
            DeviceCount = 0;
            Dashboard.DiscoveredDevices = 0;
            ConnectionQuality = ConnectionQuality.Poor;
        }
        else
        {
            foreach (var device in discovered)
            {
                Devices.Devices.Add(device);
            }
            DeviceCount = discovered.Count;
            Dashboard.DiscoveredDevices = discovered.Count;
            StatusText = "Ready";
            ConnectionQuality = DeviceCount > 0 ? ConnectionQuality.Excellent : ConnectionQuality.Fair;
        }

        UpdateDeviceInfo();
    }

    private void UpdateDeviceInfo()
    {
        if (_bridge == null) return;

        try
        {
            var (deviceName, port, peers) = _bridge.GetStatusInfo();
            DeviceInfo = $"{deviceName} | {peers} connections";
            ConnectAddress = $"Listening: 0.0.0.0:{port}";
            Dashboard.ActiveConnections = peers;
        }
        catch
        {
            DeviceInfo = "UniLinker";
        }
    }

    public void NavigateTo(int index)
    {
        CurrentNavIndex = index;
        StatusText = index switch
        {
            0 => "Dashboard",
            1 => "Devices",
            2 => "Share",
            3 => "Settings",
            _ => "Ready"
        };
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/UniLinker.WinUI/ViewModels/MainViewModel.cs
git commit -m "feat: add MainViewModel for WinUI3 app"
```

---

### Task 6: Create DashboardViewModel

**Files:**
- Create: `src/UniLinker.WinUI/ViewModels/DashboardViewModel.cs`

- [ ] **Step 1: Create DashboardViewModel.cs**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace UniLinker.WinUI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty] private string _welcomeText = "Welcome to UniLinker";
    [ObservableProperty] private string _subtitle = "Cross-device low-latency screen sharing";

    [ObservableProperty] private int _activeConnections = 0;
    [ObservableProperty] private int _discoveredDevices = 0;
    [ObservableProperty] private string _serverStatus = "Running";
    [ObservableProperty] private bool _isServerRunning = true;
}
```

- [ ] **Step 2: Commit**

```bash
git add src/UniLinker.WinUI/ViewModels/DashboardViewModel.cs
git commit -m "feat: add DashboardViewModel for WinUI3 app"
```

---

### Task 7: Create DevicesViewModel

**Files:**
- Create: `src/UniLinker.WinUI/ViewModels/DevicesViewModel.cs`

- [ ] **Step 1: Create DevicesViewModel.cs**

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using UniLinker.Plugin.Sdk;

namespace UniLinker.WinUI.ViewModels;

public partial class DevicesViewModel : ObservableObject
{
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isScanning = false;
    [ObservableProperty] private string _statusMessage = "Click scan to discover LAN devices";
    [ObservableProperty] private int _discoveryMode = 0; // 0: Auto, 1: LAN, 2: Manual IP

    [ObservableProperty] private string _manualIp = "";
    [ObservableProperty] private string _manualPort = "9527";

    public ObservableCollection<PeerInfo> Devices { get; } = new();

    private readonly DispatcherQueue _dispatcherQueue;

    public DevicesViewModel()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    [RelayCommand]
    private void StartScan()
    {
        IsScanning = true;
        StatusMessage = "Scanning LAN...";

        Task.Run(async () =>
        {
            await Task.Delay(2000);
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsScanning = false;
                StatusMessage = $"Scan complete, found {Devices.Count} devices";
            });
        });
    }

    [RelayCommand]
    private void ConnectToDevice(PeerInfo? device)
    {
        if (device == null) return;
        StatusMessage = $"Connecting to {device.Name}...";
    }

    [RelayCommand]
    private void ConnectManual()
    {
        if (string.IsNullOrEmpty(ManualIp)) return;
        StatusMessage = $"Connecting to {ManualIp}:{ManualPort}...";
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/UniLinker.WinUI/ViewModels/DevicesViewModel.cs
git commit -m "feat: add DevicesViewModel for WinUI3 app"
```

---

### Task 8: Create ShareViewModel

**Files:**
- Create: `src/UniLinker.WinUI/ViewModels/ShareViewModel.cs`

- [ ] **Step 1: Create ShareViewModel.cs**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UniLinker.WinUI.ViewModels;

public partial class ShareViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSharing = false;
    [ObservableProperty] private string _shareAddress = "Waiting to start...";
    [ObservableProperty] private int _viewerCount = 0;
    [ObservableProperty] private string _statusText = "Click start to share your screen";

    [ObservableProperty] private int _resolutionIndex = 0; // 0: 1080p, 1: 2K, 2: 4K
    [ObservableProperty] private int _frameRateIndex = 0; // 0: 30fps, 1: 60fps
    [ObservableProperty] private int _bitrateIndex = 1;   // 0: 10M, 1: 15M, 2: 20M

    public string ResolutionLabel => ResolutionIndex switch
    {
        0 => "1920x1080",
        1 => "2560x1440",
        2 => "3840x2160",
        _ => "1920x1080"
    };

    public int FrameRate => FrameRateIndex == 0 ? 30 : 60;
    public int Bitrate => BitrateIndex switch
    {
        0 => 10000,
        1 => 15000,
        2 => 20000,
        _ => 15000
    };

    [RelayCommand]
    private void StartShare()
    {
        IsSharing = true;
        ShareAddress = "192.168.1.100:9527";
        StatusText = "Sharing screen...";
    }

    [RelayCommand]
    private void StopShare()
    {
        IsSharing = false;
        ShareAddress = "Waiting to start...";
        ViewerCount = 0;
        StatusText = "Share stopped";
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/UniLinker.WinUI/ViewModels/ShareViewModel.cs
git commit -m "feat: add ShareViewModel for WinUI3 app"
```

---

### Task 9: Create SettingsViewModel

**Files:**
- Create: `src/UniLinker.WinUI/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Create SettingsViewModel.cs**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using UniLinker.WinUI.Services;

namespace UniLinker.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _deviceName = "UniLinker-PC";
    [ObservableProperty] private int _httpPort = 9527;
    [ObservableProperty] private bool _autoStart = true;
    [ObservableProperty] private bool _minimizeToTray = true;

    [ObservableProperty] private int _defaultResolution = 0;
    [ObservableProperty] private int _defaultFps = 0;
    [ObservableProperty] private int _defaultBitrate = 1;

    private int _themeIndex = 0;
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (SetProperty(ref _themeIndex, value))
            {
                ApplyTheme(value);
            }
        }
    }

    private void ApplyTheme(int index)
    {
        if (App.Current is App app)
        {
            app.RequestedTheme = index switch
            {
                0 => ApplicationTheme.Dark,
                1 => ApplicationTheme.Light,
                _ => ApplicationTheme.Dark
            };
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var config = new
        {
            DeviceName,
            HttpPort,
            AutoStart,
            MinimizeToTray
        };
        App.Services.Bridge.SaveConfig(System.Text.Json.JsonSerializer.Serialize(config));
    }

    [RelayCommand]
    private void ResetSettings()
    {
        DeviceName = Environment.MachineName;
        HttpPort = 9527;
        AutoStart = true;
        MinimizeToTray = true;
        ThemeIndex = 0;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/UniLinker.WinUI/ViewModels/SettingsViewModel.cs
git commit -m "feat: add SettingsViewModel for WinUI3 app"
```

---

## Phase 2: MainWindow and Pages

### Task 10: Create MainWindow

**Files:**
- Create: `src/UniLinker.WinUI/MainWindow.xaml`
- Create: `src/UniLinker.WinUI/MainWindow.xaml.cs`

- [ ] **Step 1: Create MainWindow.xaml**

```xml
<winex:WindowEx
    x:Class="UniLinker.WinUI.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:UniLinker.WinUI"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:winex="using:WinUIEx"
    mc:Ignorable="d"
    Title="UniLinker"
    Width="1280" Height="800"
    MinWidth="1024" MinHeight="640">

    <Grid>
        <NavigationView
            x:Name="NavView"
            IsBackButtonVisible="Collapsed"
            IsSettingsVisible="False"
            SelectedItem="{x:Bind ViewModel.CurrentNavItem, Mode=TwoWay}"
            ItemInvoked="NavView_ItemInvoked"
            PaneDisplayMode="Left">

            <NavigationView.MenuItems>
                <NavigationViewItem Content="Dashboard" Tag="Dashboard">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE80F;"/>
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="Devices" Tag="Devices">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE970;"/>
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="Share" Tag="Share">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE72D;"/>
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="Settings" Tag="Settings">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE713;"/>
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
            </NavigationView.MenuItems>

            <NavigationView.PaneFooter>
                <StackPanel Spacing="8" Margin="12">
                    <InfoBar
                        Severity="Success"
                        IsOpen="True"
                        IsClosable="False"
                        Title="Server Online"/>
                    <TextBlock
                        Text="{x:Bind ViewModel.ConnectAddress, Mode=OneWay}"
                        Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                        FontSize="11"/>
                </StackPanel>
            </NavigationView.PaneFooter>

            <Frame x:Name="ContentFrame"/>

        </NavigationView>
    </Grid>
</winex:WindowEx>
```

- [ ] **Step 2: Create MainWindow.xaml.cs**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UniLinker.WinUI.Services;
using UniLinker.WinUI.ViewModels;
using UniLinker.WinUI.Views;

namespace UniLinker.WinUI;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel(App.Services.Bridge);
        App.Services.Navigation.Initialize(ContentFrame);

        // Navigate to Dashboard on load
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer == null) return;

        var tag = args.InvokedItemContainer.Tag?.ToString();
        var pageType = tag switch
        {
            "Dashboard" => typeof(DashboardPage),
            "Devices" => typeof(DevicesPage),
            "Share" => typeof(SharePage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage)
        };

        ViewModel.NavigateTo(tag switch
        {
            "Dashboard" => 0,
            "Devices" => 1,
            "Share" => 2,
            "Settings" => 3,
            _ => 0
        });

        ContentFrame.Navigate(pageType);
    }
}
```

- [ ] **Step 3: Add WinUIEx package for WindowEx**

Update `UniLinker.WinUI.csproj` to add:

```xml
<PackageReference Include="WinUIEx" Version="2.5.0" />
```

- [ ] **Step 4: Commit**

```bash
git add src/UniLinker.WinUI/MainWindow.xaml src/UniLinker.WinUI/MainWindow.xaml.cs src/UniLinker.WinUI/UniLinker.WinUI.csproj
git commit -m "feat: add MainWindow with NavigationView shell"
```

---

### Task 11: Create DashboardPage

**Files:**
- Create: `src/UniLinker.WinUI/Views/DashboardPage.xaml`
- Create: `src/UniLinker.WinUI/Views/DashboardPage.xaml.cs`

- [ ] **Step 1: Create DashboardPage.xaml**

```xml
<Page
    x:Class="UniLinker.WinUI.Views.DashboardPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    mc:Ignorable="d"
    Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">

    <ScrollViewer Padding="24">
        <StackPanel Spacing="16">
            <!-- Header -->
            <TextBlock Text="Dashboard" Style="{StaticResource TitleTextBlockStyle}"/>

            <!-- Status Card -->
            <InfoBar
                Severity="Success"
                IsOpen="True"
                IsClosable="False"
                Title="System Ready">
                <InfoBar.Content>
                    <StackPanel>
                        <TextBlock Text="Device: DESKTOP-ABC | Connections: 3 active"/>
                    </StackPanel>
                </InfoBar.Content>
            </InfoBar>

            <!-- Stats Grid -->
            <Grid ColumnSpacing="16" RowSpacing="16">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>

                <!-- Discovered Devices -->
                <Card Grid.Column="0" Padding="16">
                    <StackPanel Spacing="8">
                        <TextBlock Text="Discovered" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
                        <TextBlock Text="5" Style="{StaticResource HeaderTextBlockStyle}"/>
                        <TextBlock Text="devices" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
                    </StackPanel>
                </Card>

                <!-- Active Connections -->
                <Card Grid.Column="1" Padding="16">
                    <StackPanel Spacing="8">
                        <TextBlock Text="Active" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
                        <TextBlock Text="3" Style="{StaticResource HeaderTextBlockStyle}"/>
                        <TextBlock Text="connections" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
                    </StackPanel>
                </Card>

                <!-- Share Sessions -->
                <Card Grid.Column="2" Padding="16">
                    <StackPanel Spacing="8">
                        <TextBlock Text="Share" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
                        <TextBlock Text="1" Style="{StaticResource HeaderTextBlockStyle}"/>
                        <TextBlock Text="session" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
                    </StackPanel>
                </Card>
            </Grid>

            <!-- Recent Devices -->
            <TextBlock Text="Recent Devices" Style="{StaticResource SubtitleTextBlockStyle}" Margin="0,16,0,0"/>
            <ListView ItemsSource="{x:Bind ViewModel.Devices.Devices, Mode=OneWay}">
                <ListView.ItemTemplate>
                    <DataTemplate x:DataType="vm:PeerInfo">
                        <Grid ColumnSpacing="12" Padding="8">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>

                            <FontIcon Grid.Column="0" Glyph="&#xE970;" FontSize="24"/>
                            <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                <TextBlock Text="{x:Bind Name}" FontWeight="Medium"/>
                                <TextBlock Text="{x:Bind IpAddress}" Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12"/>
                            </StackPanel>
                            <Border Grid.Column="2"
                                    Background="{ThemeResource SystemFillColorSuccessBrush}"
                                    CornerRadius="4" Padding="8,4">
                                <TextBlock Text="Online" Foreground="White" FontSize="11"/>
                            </Border>
                            <Button Grid.Column="3" Content="View" Margin="8,0,0,0"/>
                        </Grid>
                    </DataTemplate>
                </ListView.ItemTemplate>
            </ListView>
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 2: Create DashboardPage.xaml.cs**

```csharp
using Microsoft.UI.Xaml.Controls;
using UniLinker.WinUI.ViewModels;

namespace UniLinker.WinUI.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel => App.Services.Bridge != null
        ? new DashboardViewModel()
        : new DashboardViewModel();

    public DashboardPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/UniLinker.WinUI/Views/DashboardPage.xaml src/UniLinker.WinUI/Views/DashboardPage.xaml.cs
git commit -m "feat: add DashboardPage with stats and device list"
```

---

### Task 12: Create DevicesPage

**Files:**
- Create: `src/UniLinker.WinUI/Views/DevicesPage.xaml`
- Create: `src/UniLinker.WinUI/Views/DevicesPage.xaml.cs`

- [ ] **Step 1: Create DevicesPage.xaml**

```xml
<Page
    x:Class="UniLinker.WinUI.Views.DevicesPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    mc:Ignorable="d"
    Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">

    <Grid Padding="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Header -->
        <Grid Grid.Row="0" ColumnSpacing="12" Margin="0,0,0,16">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <TextBlock Grid.Column="0" Text="Devices" Style="{StaticResource TitleTextBlockStyle}"/>
            <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
                <TextBox PlaceholderText="Search devices..." Width="200"/>
                <Button Command="{x:Bind ViewModel.StartScanCommand}">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <FontIcon Glyph="&#xE721;" FontSize="14"/>
                        <TextBlock Text="Scan"/>
                    </StackPanel>
                </Button>
            </StackPanel>
        </Grid>

        <!-- Discovery Mode -->
        <RadioButtons Grid.Row="1" Margin="0,0,0,16"
                      SelectedIndex="{x:Bind ViewModel.DiscoveryMode, Mode=TwoWay}"
                      Orientation="Horizontal">
            <RadioButton Content="Auto-discover"/>
            <RadioButton Content="LAN"/>
            <RadioButton Content="Manual IP"/>
        </RadioButtons>

        <!-- Device Grid -->
        <GridView Grid.Row="2"
                  ItemsSource="{x:Bind ViewModel.Devices, Mode=OneWay}"
                  SelectionMode="None">
            <GridView.ItemTemplate>
                <DataTemplate x:DataType="vm:PeerInfo">
                    <Grid Width="200" Height="140" Padding="12"
                          Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                          CornerRadius="8">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="*"/>
                            <RowDefinition Height="Auto"/>
                        </Grid.RowDefinitions>

                        <FontIcon Grid.Row="0" Glyph="&#xE970;" FontSize="32" HorizontalAlignment="Left"/>
                        <StackPanel Grid.Row="1" VerticalAlignment="Center">
                            <TextBlock Text="{x:Bind Name}" FontWeight="Medium" TextTrimming="CharacterEllipsis"/>
                            <TextBlock Text="{x:Bind IpAddress}" Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="11"/>
                        </StackPanel>
                        <Button Grid.Row="2" Content="Connect" HorizontalAlignment="Stretch"/>
                    </Grid>
                </DataTemplate>
            </GridView.ItemTemplate>
        </GridView>

        <!-- Manual IP Expander -->
        <Expander Grid.Row="3" Header="Manual Connection" HorizontalAlignment="Stretch">
            <Grid ColumnSpacing="12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <TextBox Grid.Column="0"
                         PlaceholderText="IP Address"
                         Text="{x:Bind ViewModel.ManualIp, Mode=TwoWay}"/>
                <TextBox Grid.Column="1"
                         PlaceholderText="Port"
                         Text="{x:Bind ViewModel.ManualPort, Mode=TwoWay}"
                         Width="100"/>
                <Button Grid.Column="2" Content="Connect" Command="{x:Bind ViewModel.ConnectManualCommand}"/>
            </Grid>
        </Expander>
    </Grid>
</Page>
```

- [ ] **Step 2: Create DevicesPage.xaml.cs**

```csharp
using Microsoft.UI.Xaml.Controls;
using UniLinker.WinUI.ViewModels;

namespace UniLinker.WinUI.Views;

public sealed partial class DevicesPage : Page
{
    public DevicesViewModel ViewModel { get; }

    public DevicesPage()
    {
        InitializeComponent();
        ViewModel = new DevicesViewModel();
        DataContext = ViewModel;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/UniLinker.WinUI/Views/DevicesPage.xaml src/UniLinker.WinUI/Views/DevicesPage.xaml.cs
git commit -m "feat: add DevicesPage with discovery and manual connection"
```

---

### Task 13: Create SharePage

**Files:**
- Create: `src/UniLinker.WinUI/Views/SharePage.xaml`
- Create: `src/UniLinker.WinUI/Views/SharePage.xaml.cs`

- [ ] **Step 1: Create SharePage.xaml**

```xml
<Page
    x:Class="UniLinker.WinUI.Views.SharePage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    mc:Ignorable="d"
    Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">

    <Grid Padding="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Header -->
        <TextBlock Grid.Row="0" Text="Share" Style="{StaticResource TitleTextBlockStyle}" Margin="0,0,0,16"/>

        <!-- Preview Card -->
        <Card Grid.Row="1" VerticalAlignment="Center" HorizontalAlignment="Center">
            <StackPanel Spacing="16" Padding="24">
                <!-- Preview Area -->
                <Border Width="480" Height="270"
                        Background="{ThemeResource CardBackgroundFillColorSecondaryBrush}"
                        CornerRadius="8">
                    <TextBlock Text="Screen Preview" HorizontalAlignment="Center" VerticalAlignment="Center"
                               Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
                </Border>

                <!-- Status -->
                <TextBlock Text="{x:Bind ViewModel.StatusText, Mode=OneWay}"
                           HorizontalAlignment="Center"
                           Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>

                <!-- Address -->
                <TextBlock Text="{x:Bind ViewModel.ShareAddress, Mode=OneWay}"
                           HorizontalAlignment="Center"
                           Style="{StaticResource SubtitleTextBlockStyle}"/>

                <!-- Controls -->
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Spacing="12">
                    <Button Content="Start Share"
                            Command="{x:Bind ViewModel.StartShareCommand}"
                            Visibility="{x:Bind ViewModel.IsSharing, Mode=OneWay, Converter={StaticResource InverseBoolToVisibilityConverter}}"
                            Style="{StaticResource AccentButtonStyle}"/>
                    <Button Content="Stop Share"
                            Command="{x:Bind ViewModel.StopShareCommand}"
                            Visibility="{x:Bind ViewModel.IsSharing, Mode=OneWay}"/>
                </StackPanel>
            </StackPanel>
        </Card>

        <!-- Settings Expander -->
        <Expander Grid.Row="2" Header="Advanced Settings" HorizontalAlignment="Stretch">
            <Grid ColumnSpacing="16">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>

                <StackPanel Grid.Column="0">
                    <TextBlock Text="Resolution" Margin="0,0,0,8"/>
                    <ComboBox SelectedIndex="{x:Bind ViewModel.ResolutionIndex, Mode=TwoWay}" HorizontalAlignment="Stretch">
                        <x:String>1080p (1920x1080)</x:String>
                        <x:String>2K (2560x1440)</x:String>
                        <x:String>4K (3840x2160)</x:String>
                    </ComboBox>
                </StackPanel>

                <StackPanel Grid.Column="1">
                    <TextBlock Text="Frame Rate" Margin="0,0,0,8"/>
                    <ComboBox SelectedIndex="{x:Bind ViewModel.FrameRateIndex, Mode=TwoWay}" HorizontalAlignment="Stretch">
                        <x:String>30 fps</x:String>
                        <x:String>60 fps</x:String>
                    </ComboBox>
                </StackPanel>

                <StackPanel Grid.Column="2">
                    <TextBlock Text="Bitrate" Margin="0,0,0,8"/>
                    <ComboBox SelectedIndex="{x:Bind ViewModel.BitrateIndex, Mode=TwoWay}" HorizontalAlignment="Stretch">
                        <x:String>10 Mbps</x:String>
                        <x:String>15 Mbps</x:String>
                        <x:String>20 Mbps</x:String>
                    </ComboBox>
                </StackPanel>
            </Grid>
        </Expander>
    </Grid>
</Page>
```

- [ ] **Step 2: Create SharePage.xaml.cs**

```csharp
using Microsoft.UI.Xaml.Controls;
using UniLinker.WinUI.ViewModels;

namespace UniLinker.WinUI.Views;

public sealed partial class SharePage : Page
{
    public ShareViewModel ViewModel { get; }

    public SharePage()
    {
        InitializeComponent();
        ViewModel = new ShareViewModel();
        DataContext = ViewModel;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/UniLinker.WinUI/Views/SharePage.xaml src/UniLinker.WinUI/Views/SharePage.xaml.cs
git commit -m "feat: add SharePage with preview and settings"
```

---

### Task 14: Create SettingsPage

**Files:**
- Create: `src/UniLinker.WinUI/Views/SettingsPage.xaml`
- Create: `src/UniLinker.WinUI/Views/SettingsPage.xaml.cs`

- [ ] **Step 1: Create SettingsPage.xaml**

```xml
<Page
    x:Class="UniLinker.WinUI.Views.SettingsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    mc:Ignorable="d"
    Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">

    <ScrollViewer Padding="24">
        <StackPanel Spacing="16">
            <!-- Header -->
            <TextBlock Text="Settings" Style="{StaticResource TitleTextBlockStyle}"/>

            <!-- General Settings -->
            <Expander Header="General" HorizontalAlignment="Stretch" IsExpanded="True">
                <StackPanel Spacing="12">
                    <Grid ColumnSpacing="12">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="Device Name" VerticalAlignment="Center"/>
                        <TextBox Grid.Column="1" Text="{x:Bind ViewModel.DeviceName, Mode=TwoWay}" Width="200"/>
                    </Grid>

                    <Grid ColumnSpacing="12">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="HTTP Port" VerticalAlignment="Center"/>
                        <NumberBox Grid.Column="1" Value="{x:Bind ViewModel.HttpPort, Mode=TwoWay}" Width="100" Minimum="1024" Maximum="65535"/>
                    </Grid>

                    <ToggleSwitch Header="Auto-start on login" IsOn="{x:Bind ViewModel.AutoStart, Mode=TwoWay}"/>
                    <ToggleSwitch Header="Minimize to tray" IsOn="{x:Bind ViewModel.MinimizeToTray, Mode=TwoWay}"/>
                </StackPanel>
            </Expander>

            <!-- Appearance -->
            <Expander Header="Appearance" HorizontalAlignment="Stretch">
                <StackPanel Spacing="12">
                    <TextBlock Text="Theme"/>
                    <RadioButtons SelectedIndex="{x:Bind ViewModel.ThemeIndex, Mode=TwoWay}">
                        <RadioButton Content="Dark"/>
                        <RadioButton Content="Light"/>
                        <RadioButton Content="System"/>
                    </RadioButtons>
                </StackPanel>
            </Expander>

            <!-- Plugins -->
            <Expander Header="Plugins" HorizontalAlignment="Stretch">
                <ListView SelectionMode="None">
                    <ListViewItem>
                        <Grid ColumnSpacing="12" Padding="8">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <FontIcon Grid.Column="0" Glyph="&#xE8B2;" FontSize="24"/>
                            <StackPanel Grid.Column="1">
                                <TextBlock Text="Screen Mirror" FontWeight="Medium"/>
                                <TextBlock Text="v1.0.0" Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="11"/>
                            </StackPanel>
                            <ToggleSwitch Grid.Column="2" IsOn="True"/>
                        </Grid>
                    </ListViewItem>
                </ListView>
            </Expander>

            <!-- Actions -->
            <StackPanel Orientation="Horizontal" Spacing="12" HorizontalAlignment="Right" Margin="0,16,0,0">
                <Button Content="Reset to Defaults" Command="{x:Bind ViewModel.ResetSettingsCommand}"/>
                <Button Content="Save" Style="{StaticResource AccentButtonStyle}" Command="{x:Bind ViewModel.SaveSettingsCommand}"/>
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 2: Create SettingsPage.xaml.cs**

```csharp
using Microsoft.UI.Xaml.Controls;
using UniLinker.WinUI.ViewModels;

namespace UniLinker.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = new SettingsViewModel();
        DataContext = ViewModel;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/UniLinker.WinUI/Views/SettingsPage.xaml src/UniLinker.WinUI/Views/SettingsPage.xaml.cs
git commit -m "feat: add SettingsPage with general and appearance settings"
```

---

## Phase 3: Controls and Polish

### Task 15: Create SignalStrengthControl

**Files:**
- Create: `src/UniLinker.WinUI/Controls/SignalStrengthControl.xaml`
- Create: `src/UniLinker.WinUI/Controls/SignalStrengthControl.xaml.cs`

- [ ] **Step 1: Create SignalStrengthControl.xaml**

```xml
<UserControl
    x:Class="UniLinker.WinUI.Controls.SignalStrengthControl"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    mc:Ignorable="d"
    Height="16" Width="24">

    <Grid ColumnSpacing="2">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="4"/>
            <ColumnDefinition Width="4"/>
            <ColumnDefinition Width="4"/>
            <ColumnDefinition Width="4"/>
        </Grid.ColumnDefinitions>

        <Border x:Name="Bar1" Grid.Column="0" Height="6" VerticalAlignment="Bottom" CornerRadius="1"/>
        <Border x:Name="Bar2" Grid.Column="1" Height="10" VerticalAlignment="Bottom" CornerRadius="1"/>
        <Border x:Name="Bar3" Grid.Column="2" Height="14" VerticalAlignment="Bottom" CornerRadius="1"/>
        <Border x:Name="Bar4" Grid.Column="3" Height="18" VerticalAlignment="Bottom" CornerRadius="1"/>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create SignalStrengthControl.xaml.cs**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UniLinker.WinUI.Models;

namespace UniLinker.WinUI.Controls;

public sealed partial class SignalStrengthControl : UserControl
{
    public static readonly DependencyProperty QualityProperty =
        DependencyProperty.Register(nameof(Quality), typeof(ConnectionQuality), typeof(SignalStrengthControl),
            new PropertyMetadata(ConnectionQuality.Good, OnQualityChanged));

    public ConnectionQuality Quality
    {
        get => (ConnectionQuality)GetValue(QualityProperty);
        set => SetValue(QualityProperty, value);
    }

    public SignalStrengthControl()
    {
        InitializeComponent();
        UpdateBars((int)Quality);
    }

    private static void OnQualityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SignalStrengthControl)d;
        control.UpdateBars((int)e.NewValue);
    }

    private void UpdateBars(int quality)
    {
        var activeColor = Application.Current.Resources["SystemAccentColor"] as Microsoft.UI.Xaml.Media.SolidColorBrush;
        var inactiveColor = Application.Current.Resources["TextFillColorTertiaryBrush"] as Microsoft.UI.Xaml.Media.Brush;

        Bar1.Background = quality >= 1 ? activeColor : inactiveColor;
        Bar2.Background = quality >= 2 ? activeColor : inactiveColor;
        Bar3.Background = quality >= 3 ? activeColor : inactiveColor;
        Bar4.Background = quality >= 4 ? activeColor : inactiveColor;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/UniLinker.WinUI/Controls/
git commit -m "feat: add SignalStrengthControl for connection quality"
```

---

### Task 16: Add Value Converters

**Files:**
- Create: `src/UniLinker.WinUI/Converters/BooleanToVisibilityConverter.cs`
- Create: `src/UniLinker.WinUI/Converters/InverseBooleanToVisibilityConverter.cs`

- [ ] **Step 1: Create BooleanToVisibilityConverter.cs**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace UniLinker.WinUI.Converters;

public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility.Visible;
    }
}
```

- [ ] **Step 2: Create InverseBooleanToVisibilityConverter.cs**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace UniLinker.WinUI.Converters;

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility.Collapsed;
    }
}
```

- [ ] **Step 3: Register converters in App.xaml**

Update `App.xaml` to add converters:

```xml
<Application
    x:Class="UniLinker.WinUI.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:converters="using:UniLinker.WinUI.Converters">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
                <ResourceDictionary Source="Styles/AppStyles.xaml"/>
            </ResourceDictionary.MergedDictionaries>

            <converters:BooleanToVisibilityConverter x:Key="BoolToVisibilityConverter"/>
            <converters:InverseBooleanToVisibilityConverter x:Key="InverseBoolToVisibilityConverter"/>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 4: Commit**

```bash
git add src/UniLinker.WinUI/Converters/ src/UniLinker.WinUI/App.xaml
git commit -m "feat: add value converters for WinUI3 bindings"
```

---

### Task 17: Update AppStyles.xaml

**Files:**
- Modify: `src/UniLinker.WinUI/Styles/AppStyles.xaml`

- [ ] **Step 1: Update AppStyles.xaml with custom styles**

```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Card Style -->
    <Style x:Key="Card" TargetType="ContentControl">
        <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}"/>
        <Setter Property="CornerRadius" Value="8"/>
        <Setter Property="Padding" Value="16"/>
    </Style>

    <!-- Page Title Style -->
    <Style x:Key="PageTitleStyle" TargetType="TextBlock" BasedOn="{StaticResource TitleTextBlockStyle}">
        <Setter Property="Margin" Value="0,0,0,16"/>
    </Style>

</ResourceDictionary>
```

- [ ] **Step 2: Commit**

```bash
git add src/UniLinker.WinUI/Styles/AppStyles.xaml
git commit -m "feat: add custom styles for WinUI3 app"
```

---

## Phase 4: Build and Test

### Task 18: Build and Fix Compilation Errors

**Files:**
- Modify: Various files as needed

- [ ] **Step 1: Build the project**

```bash
dotnet build src/UniLinker.WinUI/UniLinker.WinUI.csproj
```

- [ ] **Step 2: Fix any compilation errors**

Common issues to check:
- Missing `using` statements
- Incorrect namespace references
- XAML type resolution errors
- Missing type declarations in `x:DataType`

- [ ] **Step 3: Commit fixes**

```bash
git add -A
git commit -m "fix: resolve WinUI3 compilation errors"
```

---

### Task 19: Test Application

**Files:**
- None (manual testing)

- [ ] **Step 1: Run the application**

```bash
dotnet run --project src/UniLinker.WinUI/UniLinker.WinUI.csproj
```

- [ ] **Step 2: Verify core functionality**

Checklist:
- [ ] Application launches without crash
- [ ] MainWindow displays with NavigationView
- [ ] Navigation between pages works
- [ ] Dashboard shows stats
- [ ] Devices page shows device grid
- [ ] Share page shows preview area
- [ ] Settings page shows options
- [ ] Theme switching works

- [ ] **Step 3: Fix any runtime issues**

- [ ] **Step 4: Commit fixes**

```bash
git add -A
git commit -m "fix: resolve WinUI3 runtime issues"
```

---

## Phase 5: Cleanup

### Task 20: Remove Old Avalonia Projects

**Files:**
- Delete: `src/UniLinker.App/`
- Delete: `src/UniLinker.UI/`
- Modify: `UniLinker.sln`

- [ ] **Step 1: Remove old projects from solution**

```bash
dotnet sln remove src/UniLinker.App/UniLinker.App.csproj
dotnet sln remove src/UniLinker.UI/UniLinker.UI.csproj
```

- [ ] **Step 2: Delete old project directories**

```bash
rm -rf src/UniLinker.App
rm -rf src/UniLinker.UI
```

- [ ] **Step 3: Commit cleanup**

```bash
git add -A
git commit -m "chore: remove old Avalonia UI projects"
```

---

### Task 21: Update Documentation

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update README.md**

Change the Windows run command from:
```bash
dotnet run --project src/UniLinker.App
```

To:
```bash
dotnet run --project src/UniLinker.WinUI
```

Update project structure section to reflect WinUI3 project.

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: update README for WinUI3"
```

---

## Success Criteria

1. [ ] Application builds without errors
2. [ ] Application runs and displays MainWindow
3. [ ] Navigation between all 4 pages works
4. [ ] Dashboard displays stats and device list
5. [ ] Devices page shows discovered devices
6. [ ] Share page allows starting/stopping share
7. [ ] Settings page allows configuration changes
8. [ ] Theme switching works (Dark/Light)
9. [ ] Old Avalonia projects removed
10. [ ] README updated
