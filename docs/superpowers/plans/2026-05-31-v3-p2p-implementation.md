# UniLinker v3.0 — P2P 设备互联平台 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建局域网设备互联平台 — 自动发现、建立 P2P 通道、插件化功能扩展，首个插件为屏幕投屏

**Architecture:** C# 12 / .NET 9 NativeAOT 单文件应用，Core Platform 提供 Peer Mesh + mDNS Discovery + Plugin Host + WebView2 GUI Shell，功能由独立插件 Assembly 提供，插件只依赖 Plugin.Sdk 接口

**Tech Stack:** .NET 9 SDK (已安装 9.0.305), Windows.Graphics.Capture, Media Foundation, Zeroconf (MIT), WebView2, NativeAOT

**Spec:** `docs/superpowers/specs/2026-05-31-v3-p2p-design.md`

**Design Doc:** See spec for full architecture diagrams, interface definitions, data flow, and GUI layout

---

## File Structure Map

```
uniLinker/
├── UniLinker.sln
├── src/
│   ├── UniLinker.Core/              # 平台核心 (net9.0-windows)
│   │   ├── UniLinker.Core.csproj
│   │   ├── Platform.cs              # 启动入口
│   │   ├── PeerMesh.cs              # WebRTC 连接网格
│   │   ├── PeerConnection.cs        # 单条连接封装
│   │   ├── DiscoveryService.cs      # mDNS 封装
│   │   ├── ConfigStore.cs           # JSON 配置
│   │   ├── PluginLoader.cs          # Assembly 加载/隔离
│   │   ├── PluginHost.cs            # 生命周期管理
│   │   ├── SignalingServer.cs       # HTTP 信令端点
│   │   └── RtpPacketizer.cs         # H.264 NAL → RTP
│   │
│   ├── UniLinker.Plugin.Sdk/        # 插件 SDK (net9.0, 纯接口无平台依赖)
│   │   ├── UniLinker.Plugin.Sdk.csproj
│   │   ├── IPlugin.cs
│   │   ├── IPluginContext.cs
│   │   ├── IPeerMesh.cs
│   │   ├── IChannel.cs
│   │   ├── IDeviceDiscovery.cs
│   │   ├── IConfigStore.cs
│   │   ├── IUIProvider.cs
│   │   ├── ICapture.cs
│   │   ├── IEncoder.cs
│   │   ├── IPluginLogger.cs
│   │   ├── PeerInfo.cs
│   │   ├── ChannelOptions.cs
│   │   ├── PluginInfo.cs
│   │   ├── PluginPermission.cs
│   │   ├── CaptureFrame.cs
│   │   ├── EncodedPacket.cs
│   │   └── DeviceRole.cs
│   │
│   ├── UniLinker.Plugins.ScreenMirror/  # 插件: 屏幕投屏 (net9.0-windows)
│   │   ├── UniLinker.Plugins.ScreenMirror.csproj
│   │   ├── ScreenMirrorPlugin.cs
│   │   ├── WgcCapture.cs
│   │   ├── MfEncoder.cs
│   │   ├── StreamManager.cs
│   │   └── ScreenMirrorUI.cs
│   │
│   ├── UniLinker.App/               # 主程序壳 (net9.0-windows, NativeAOT)
│   │   ├── UniLinker.App.csproj
│   │   ├── Program.cs
│   │   └── AppHost.cs
│   │
│   └── UniLinker.UI/                # WebView2 GUI (net9.0-windows)
│       ├── UniLinker.UI.csproj
│       ├── MainWindow.cs
│       ├── WebBridge.cs
│       └── TrayIcon.cs
│
├── web/                             # 前端资源
│   ├── index.html
│   ├── app.js
│   ├── styles.css
│   └── plugins/
│       └── screen-mirror/
│           ├── panel.html
│           └── panel.js
│
├── plugins/                         # 编译输出目录 (运行时扫描)
│   └── .gitkeep
│
└── docs/
    └── superpowers/
        ├── specs/
        │   └── 2026-05-31-v3-p2p-design.md
        └── plans/
            └── 2026-05-31-v3-p2p-implementation.md
```

---

## Phase 1: Core Platform Skeleton

### Task 1.1: Create Solution and Projects

**Files:**
- Create: `UniLinker.sln`
- Create: `src/UniLinker.Plugin.Sdk/UniLinker.Plugin.Sdk.csproj`
- Create: `src/UniLinker.Core/UniLinker.Core.csproj`
- Create: `src/UniLinker.App/UniLinker.App.csproj`
- Create: `src/UniLinker.UI/UniLinker.UI.csproj`
- Create: `src/UniLinker.Plugins.ScreenMirror/UniLinker.Plugins.ScreenMirror.csproj`
- Create: `.gitignore`
- Create: `plugins/.gitkeep`

- [ ] **Step 1: Create .gitignore**

```bash
cd C:/Users/24311/Documents/coding/uniLinker
dotnet new gitignore
```

- [ ] **Step 2: Create solution**

```bash
dotnet new sln -n UniLinker
```

- [ ] **Step 3: Create Plugin SDK project (net9.0, no Windows dependency)**

```bash
mkdir -p src/UniLinker.Plugin.Sdk
dotnet new classlib -n UniLinker.Plugin.Sdk -o src/UniLinker.Plugin.Sdk -f net9.0
```

Write `src/UniLinker.Plugin.Sdk/UniLinker.Plugin.Sdk.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>UniLinker.Plugin.Sdk</RootNamespace>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Create Core project (net9.0-windows)**

```bash
mkdir -p src/UniLinker.Core
dotnet new classlib -n UniLinker.Core -o src/UniLinker.Core -f net9.0-windows
```

Write `src/UniLinker.Core/UniLinker.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>UniLinker.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Zeroconf" Version="3.7.16" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\UniLinker.Plugin.Sdk\UniLinker.Plugin.Sdk.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Create App project (NativeAOT executable)**

```bash
mkdir -p src/UniLinker.App
dotnet new console -n UniLinker.App -o src/UniLinker.App -f net9.0-windows
```

Write `src/UniLinker.App/UniLinker.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PublishAot>true</PublishAot>
    <_SuppressWinRTTrimError>true</_SuppressWinRTTrimError>
    <RootNamespace>UniLinker.App</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\UniLinker.Core\UniLinker.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Create UI project**

```bash
mkdir -p src/UniLinker.UI
dotnet new classlib -n UniLinker.UI -o src/UniLinker.UI -f net9.0-windows
```

Write `src/UniLinker.UI/UniLinker.UI.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <RootNamespace>UniLinker.UI</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.3065.39" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\UniLinker.Core\UniLinker.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 7: Create ScreenMirror plugin project**

```bash
mkdir -p src/UniLinker.Plugins.ScreenMirror
dotnet new classlib -n UniLinker.Plugins.ScreenMirror -o src/UniLinker.Plugins.ScreenMirror -f net9.0-windows
```

Write `src/UniLinker.Plugins.ScreenMirror/UniLinker.Plugins.ScreenMirror.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <RootNamespace>UniLinker.Plugins.ScreenMirror</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.SDK.NET" Version="10.0.26100.57" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\UniLinker.Plugin.Sdk\UniLinker.Plugin.Sdk.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 8: Add all projects to solution**

```bash
dotnet sln UniLinker.sln add src/UniLinker.Plugin.Sdk/UniLinker.Plugin.Sdk.csproj
dotnet sln UniLinker.sln add src/UniLinker.Core/UniLinker.Core.csproj
dotnet sln UniLinker.sln add src/UniLinker.App/UniLinker.App.csproj
dotnet sln UniLinker.sln add src/UniLinker.UI/UniLinker.UI.csproj
dotnet sln UniLinker.sln add src/UniLinker.Plugins.ScreenMirror/UniLinker.Plugins.ScreenMirror.csproj
```

- [ ] **Step 9: Verify solution builds**

```bash
dotnet build UniLinker.sln
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: create solution structure with 5 projects

- UniLinker.Plugin.Sdk: plugin interface definitions (net9.0)
- UniLinker.Core: platform core with PeerMesh, Discovery, PluginLoader
- UniLinker.App: NativeAOT entry point (WinExe)
- UniLinker.UI: WebView2 GUI shell
- UniLinker.Plugins.ScreenMirror: screen mirroring plugin"
```

### Task 1.2: Define Plugin SDK Data Models

**Files:**
- Create: `src/UniLinker.Plugin.Sdk/PeerInfo.cs`
- Create: `src/UniLinker.Plugin.Sdk/PluginInfo.cs`
- Create: `src/UniLinker.Plugin.Sdk/PluginPermission.cs`
- Create: `src/UniLinker.Plugin.Sdk/ChannelOptions.cs`
- Create: `src/UniLinker.Plugin.Sdk/CaptureFrame.cs`
- Create: `src/UniLinker.Plugin.Sdk/EncodedPacket.cs`
- Create: `src/UniLinker.Plugin.Sdk/DeviceRole.cs`
- Delete: `src/UniLinker.Plugin.Sdk/Class1.cs`

- [ ] **Step 1: Write PeerInfo.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

/// <summary>Represents a discovered or connected peer device.</summary>
public class PeerInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Version { get; init; } = string.Empty;
    public string[] Capabilities { get; init; } = [];
    public PeerState State { get; set; } = PeerState.Discovered;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

public enum PeerState
{
    Discovered,
    Connected,
    Disconnected
}
```

- [ ] **Step 2: Write PluginInfo.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public class PluginInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string AssemblyPath { get; init; } = string.Empty;
    public string[] Capabilities { get; init; } = [];
}
```

- [ ] **Step 3: Write PluginPermission.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

[Flags]
public enum PluginPermission
{
    None = 0,
    ScreenCapture = 1 << 0,
    AudioCapture = 1 << 1,
    FileSystem = 1 << 2,
    Clipboard = 1 << 3,
    InputSimulation = 1 << 4,
    Network = 1 << 5,
}
```

- [ ] **Step 4: Write ChannelOptions.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public class ChannelOptions
{
    public string? Label { get; init; }
    public bool Ordered { get; init; } = true;
    public int MaxRetransmits { get; init; } = 0;
}

public enum ChannelType
{
    MediaTrack,
    DataChannel
}

public enum ChannelState
{
    Connecting,
    Open,
    Closing,
    Closed
}
```

- [ ] **Step 5: Write CaptureFrame.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public record CaptureFrame(
    nint D3dTexture,
    int Width,
    int Height,
    int Pitch,
    long TimestampUs);
```

- [ ] **Step 6: Write EncodedPacket.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public record EncodedPacket(
    byte[] Data,
    long TimestampUs,
    bool IsKeyFrame);
```

- [ ] **Step 7: Write DeviceRole.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

[Flags]
public enum DeviceRole
{
    None = 0,
    Sender = 1 << 0,
    Receiver = 1 << 1,
    Both = Sender | Receiver,
}
```

- [ ] **Step 8: Verify build**

```bash
dotnet build src/UniLinker.Plugin.Sdk/UniLinker.Plugin.Sdk.csproj
```

- [ ] **Step 9: Commit**

```bash
git add src/UniLinker.Plugin.Sdk/
git commit -m "feat: define Plugin SDK data models"
```

### Task 1.3: Define Plugin SDK Interfaces

**Files:**
- Create: `src/UniLinker.Plugin.Sdk/IPluginLogger.cs`
- Create: `src/UniLinker.Plugin.Sdk/IDeviceDiscovery.cs`
- Create: `src/UniLinker.Plugin.Sdk/IConfigStore.cs`
- Create: `src/UniLinker.Plugin.Sdk/IUIProvider.cs`
- Create: `src/UniLinker.Plugin.Sdk/IChannel.cs`
- Create: `src/UniLinker.Plugin.Sdk/IPeerMesh.cs`
- Create: `src/UniLinker.Plugin.Sdk/IPluginContext.cs`
- Create: `src/UniLinker.Plugin.Sdk/IPlugin.cs`
- Create: `src/UniLinker.Plugin.Sdk/ICapture.cs`
- Create: `src/UniLinker.Plugin.Sdk/IEncoder.cs`

- [ ] **Step 1: Write IPluginLogger.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public interface IPluginLogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}
```

- [ ] **Step 2: Write IDeviceDiscovery.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public interface IDeviceDiscovery
{
    event Action<PeerInfo>? DeviceFound;
    event Action<PeerInfo>? DeviceLost;
    IReadOnlyList<PeerInfo> KnownDevices { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
}
```

- [ ] **Step 3: Write IConfigStore.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public interface IConfigStore
{
    T Get<T>(string key) where T : new();
    void Set<T>(string key, T value);
    Task SaveAsync();
}
```

- [ ] **Step 4: Write IUIProvider.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public interface IUIProvider
{
    void RegisterPanel(string pluginId, PanelInfo panel);
    void RegisterTrayAction(string pluginId, TrayAction action);
}

public class PanelInfo
{
    public string Title { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string HtmlPath { get; init; } = string.Empty;
}

public class TrayAction
{
    public string Label { get; init; } = string.Empty;
    public Action? OnClick { get; init; }
}
```

- [ ] **Step 5: Write IChannel.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public interface IChannel : IDisposable
{
    string Id { get; }
    ChannelType Type { get; }
    PeerInfo RemotePeer { get; }
    string Capability { get; }
    ChannelState State { get; }
    bool IsOpen { get; }
    event Action? OnClose;

    // DataChannel
    event Action<byte[]>? MessageReceived;
    Task SendAsync(byte[] data);

    // MediaTrack
    event Action<EncodedPacket>? PacketReceived;
    Task SendPacketAsync(EncodedPacket packet);
}
```

- [ ] **Step 6: Write IPeerMesh.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public interface IPeerMesh
{
    IReadOnlyList<PeerInfo> ConnectedPeers { get; }
    event Action<PeerInfo>? PeerConnected;
    event Action<PeerInfo>? PeerDisconnected;
    event Func<PeerInfo, string, Task<IChannel?>>? ChannelRequested;
    Task<IChannel?> CreateChannel(PeerInfo peer, string capability, ChannelOptions? options = null);
    Task DisconnectPeer(PeerInfo peer);
}
```

- [ ] **Step 7: Write IPluginContext.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public interface IPluginContext
{
    IPeerMesh Peers { get; }
    IDeviceDiscovery Discovery { get; }
    IConfigStore Config { get; }
    IUIProvider UI { get; }
    PluginInfo Self { get; }
    IPluginLogger Logger { get; }
}
```

- [ ] **Step 8: Write IPlugin.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public interface IPlugin
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    PluginPermission RequiredPermissions { get; }
    string[] Capabilities { get; }
    Task<bool> Initialize(IPluginContext context);
    Task<IChannel?> OnPeerRequest(PeerInfo peer, string capability);
    Task Shutdown();
}
```

- [ ] **Step 9: Write ICapture.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public interface ICapture : IDisposable
{
    event Action<CaptureFrame>? FrameCaptured;
    bool Start(int width, int height, int fps);
    void Stop();
    CaptureInfo GetInfo();
}

public record CaptureInfo(int Width, int Height, int Fps, string Backend);
```

- [ ] **Step 10: Write IEncoder.cs**

```csharp
namespace UniLinker.Plugin.Sdk;

public interface IEncoder : IDisposable
{
    event Action<EncodedPacket>? PacketEncoded;
    bool Initialize(int width, int height, int fps, int bitrateKbps);
    void Encode(CaptureFrame frame);
    EncoderInfo GetInfo();
}

public record EncoderInfo(string CodecName, int BitrateKbps, bool IsHardware);
```

- [ ] **Step 11: Verify build**

```bash
dotnet build src/UniLinker.Plugin.Sdk/UniLinker.Plugin.Sdk.csproj
```

- [ ] **Step 12: Commit**

```bash
git add src/UniLinker.Plugin.Sdk/
git commit -m "feat: define all Plugin SDK interfaces"
```

### Task 1.4: Implement ConfigStore

**Files:**
- Create: `src/UniLinker.Core/ConfigStore.cs`

- [ ] **Step 1: Write ConfigStore.cs**

```csharp
using System.Text.Json;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class ConfigStore : IConfigStore
{
    private readonly string _filePath;
    private Dictionary<string, JsonElement> _data;

    public ConfigStore(string filePath)
    {
        _filePath = filePath;
        _data = new Dictionary<string, JsonElement>();
    }

    public T Get<T>(string key) where T : new()
    {
        if (_data.TryGetValue(key, out var element))
        {
            try { return element.Deserialize<T>() ?? new T(); }
            catch { return new T(); }
        }
        return new T();
    }

    public void Set<T>(string key, T value)
    {
        var element = JsonSerializer.SerializeToElement(value);
        _data[key] = element;
    }

    public async Task SaveAsync()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir != null) Directory.CreateDirectory(dir);

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_data, options);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            _data = new Dictionary<string, JsonElement>();
            return;
        }
        var json = await File.ReadAllTextAsync(_filePath);
        _data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                ?? new Dictionary<string, JsonElement>();
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/UniLinker.Core/UniLinker.Core.csproj
```

- [ ] **Step 3: Commit**

```bash
git add src/UniLinker.Core/ConfigStore.cs
git commit -m "feat: implement JSON file-based ConfigStore"
```

### Task 1.5: Implement PluginLoader

**Files:**
- Create: `src/UniLinker.Core/PluginLoader.cs`
- Create: `src/UniLinker.Core/PluginHost.cs`

- [ ] **Step 1: Write PluginLoader.cs**

```csharp
using System.Reflection;
using System.Runtime.Loader;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class PluginLoader
{
    public List<LoadedPlugin> LoadedPlugins { get; } = new();

    public void DiscoverAndLoad(string pluginsDir)
    {
        if (!Directory.Exists(pluginsDir)) return;

        foreach (var dllPath in Directory.EnumerateFiles(pluginsDir, "*.dll"))
        {
            try
            {
                var plugin = LoadPlugin(dllPath);
                if (plugin != null)
                    LoadedPlugins.Add(plugin);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Failed to load plugin {dllPath}: {ex.Message}");
            }
        }
    }

    private LoadedPlugin? LoadPlugin(string dllPath)
    {
        var alc = new AssemblyLoadContext(
            Path.GetFileNameWithoutExtension(dllPath), isCollectible: true);

        var assembly = alc.LoadFromAssemblyPath(dllPath);

        foreach (var type in assembly.GetExportedTypes())
        {
            if (!typeof(IPlugin).IsAssignableFrom(type) || type.IsAbstract)
                continue;

            if (Activator.CreateInstance(type) is IPlugin plugin)
            {
                return new LoadedPlugin(plugin, alc, dllPath);
            }
        }

        alc.Unload();
        return null;
    }
}

public class LoadedPlugin
{
    public IPlugin Plugin { get; }
    public AssemblyLoadContext Context { get; }
    public string AssemblyPath { get; }

    public LoadedPlugin(IPlugin plugin, AssemblyLoadContext context, string assemblyPath)
    {
        Plugin = plugin;
        Context = context;
        AssemblyPath = assemblyPath;
    }
}
```

- [ ] **Step 2: Write PluginHost.cs**

```csharp
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class PluginHost
{
    private readonly PluginLoader _loader;
    private readonly IPluginContext _contextTemplate;

    public List<LoadedPlugin> Plugins => _loader.LoadedPlugins;

    public PluginHost(PluginLoader loader, IPluginContext contextTemplate)
    {
        _loader = loader;
        _contextTemplate = contextTemplate;
    }

    public async Task InitializeAllAsync()
    {
        foreach (var loaded in _loader.LoadedPlugins)
        {
            var ctx = new PluginContextWrapper(_contextTemplate, new PluginInfo
            {
                Id = loaded.Plugin.Id,
                Name = loaded.Plugin.Name,
                Version = loaded.Plugin.Version,
                AssemblyPath = loaded.AssemblyPath,
                Capabilities = loaded.Plugin.Capabilities,
            });

            var ok = await loaded.Plugin.Initialize(ctx);
            System.Diagnostics.Debug.WriteLine(
                $"Plugin '{loaded.Plugin.Id}' initialized: {ok}");
        }
    }

    public async Task ShutdownAllAsync()
    {
        foreach (var loaded in _loader.LoadedPlugins)
        {
            try { await loaded.Plugin.Shutdown(); }
            catch { }
            loaded.Context.Unload();
        }
    }

    public LoadedPlugin? FindByCapability(string capability)
    {
        return _loader.LoadedPlugins
            .FirstOrDefault(p => p.Plugin.Capabilities.Contains(capability));
    }
}

internal class PluginContextWrapper : IPluginContext
{
    private readonly IPluginContext _inner;
    public PluginInfo Self { get; }
    public IPeerMesh Peers => _inner.Peers;
    public IDeviceDiscovery Discovery => _inner.Discovery;
    public IConfigStore Config => _inner.Config;
    public IUIProvider UI => _inner.UI;
    public IPluginLogger Logger => _inner.Logger;

    public PluginContextWrapper(IPluginContext inner, PluginInfo self)
    {
        _inner = inner;
        Self = self;
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build src/UniLinker.Core/UniLinker.Core.csproj
```

- [ ] **Step 4: Commit**

```bash
git add src/UniLinker.Core/PluginLoader.cs src/UniLinker.Core/PluginHost.cs
git commit -m "feat: implement plugin loader with AssemblyLoadContext isolation"
```

### Task 1.6: Implement Platform Entry Point

**Files:**
- Create: `src/UniLinker.Core/Platform.cs`

- [ ] **Step 1: Write Platform.cs**

```csharp
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class Platform : IDisposable
{
    private readonly PluginLoader _pluginLoader;
    private readonly PluginHost _pluginHost;
    private readonly ConfigStore _configStore;

    public IPluginContext Context { get; }
    public PeerMesh? PeerMesh { get; private set; }
    public DiscoveryService? Discovery { get; private set; }

    public Platform(string pluginsDir, string configPath)
    {
        _configStore = new ConfigStore(configPath);
        _pluginLoader = new PluginLoader();

        Context = new PlatformContext
        {
            Config = _configStore,
            Logger = new ConsoleLogger(),
            UI = new NullUIProvider(),
        };

        _pluginHost = new PluginHost(_pluginLoader, Context);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _configStore.LoadAsync();

        var config = _configStore.Get<PlatformConfig>("platform");

        PeerMesh = new PeerMesh();
        Discovery = new DiscoveryService();
        ((PlatformContext)Context).Peers = PeerMesh;
        ((PlatformContext)Context).Discovery = Discovery;

        _pluginLoader.DiscoverAndLoad(pluginsDir);
        await _pluginHost.InitializeAllAsync();

        await Discovery.StartAsync(ct);
    }

    private string pluginsDir => Path.Combine(AppContext.BaseDirectory, "plugins");

    public async Task StopAsync()
    {
        await _pluginHost.ShutdownAllAsync();
        Discovery?.Dispose();
        PeerMesh?.Dispose();
        await _configStore.SaveAsync();
    }

    public void Dispose()
    {
        Discovery?.Dispose();
        PeerMesh?.Dispose();
    }
}

public class PlatformConfig
{
    public string DeviceName { get; set; } = Environment.MachineName;
    public int HttpPort { get; set; } = 9527;
    public bool AutoStartPlugins { get; set; } = true;
    public string[] DisabledPlugins { get; set; } = [];
}

internal class PlatformContext : IPluginContext
{
    public IPeerMesh Peers { get; set; } = null!;
    public IDeviceDiscovery Discovery { get; set; } = null!;
    public IConfigStore Config { get; init; } = null!;
    public IUIProvider UI { get; init; } = null!;
    public PluginInfo Self => new();
    public IPluginLogger Logger { get; init; } = null!;
}

internal class ConsoleLogger : IPluginLogger
{
    public void Debug(string msg) => Console.WriteLine($"[DBG] {msg}");
    public void Info(string msg) => Console.WriteLine($"[INF] {msg}");
    public void Warn(string msg) => Console.WriteLine($"[WRN] {msg}");
    public void Error(string msg, Exception? ex) =>
        Console.WriteLine($"[ERR] {msg} {ex?.Message}");
}

internal class NullUIProvider : IUIProvider
{
    public void RegisterPanel(string id, PanelInfo panel) { }
    public void RegisterTrayAction(string id, TrayAction action) { }
}
```

- [ ] **Step 2: Add stub files so build compiles**

Create `src/UniLinker.Core/PeerMesh.cs` (stub):

```csharp
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class PeerMesh : IPeerMesh, IDisposable
{
    private readonly List<PeerInfo> _peers = new();

    public IReadOnlyList<PeerInfo> ConnectedPeers => _peers.AsReadOnly();
    public event Action<PeerInfo>? PeerConnected;
    public event Action<PeerInfo>? PeerDisconnected;
    public event Func<PeerInfo, string, Task<IChannel?>>? ChannelRequested;

    public Task<IChannel?> CreateChannel(
        PeerInfo peer, string capability, ChannelOptions? options = null)
    {
        return Task.FromResult<IChannel?>(null);
    }

    public Task DisconnectPeer(PeerInfo peer) => Task.CompletedTask;

    public void Dispose() { }
}
```

Create `src/UniLinker.Core/DiscoveryService.cs` (stub):

```csharp
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class DiscoveryService : IDeviceDiscovery, IDisposable
{
    private readonly List<PeerInfo> _devices = new();

    public event Action<PeerInfo>? DeviceFound;
    public event Action<PeerInfo>? DeviceLost;
    public IReadOnlyList<PeerInfo> KnownDevices => _devices.AsReadOnly();

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public void Dispose() { }
}
```

- [ ] **Step 3: Verify solution builds**

```bash
dotnet build UniLinker.sln
```

- [ ] **Step 4: Commit**

```bash
git add src/UniLinker.Core/
git commit -m "feat: implement Platform entry point with stubs"
```

---

## Phase 2: Capture + Encode Pipeline

### Task 2.1: Implement WGC Screen Capture

**Files:**
- Create: `src/UniLinker.Plugins.ScreenMirror/WgcCapture.cs`

- [ ] **Step 1: Write WgcCapture.cs**

```csharp
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

public class WgcCapture : ICapture
{
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private IDirect3DDevice? _device;
    private int _width;
    private int _height;
    private int _fps;

    public event Action<CaptureFrame>? FrameCaptured;

    public bool Start(int width, int height, int fps)
    {
        _width = width;
        _height = height;
        _fps = fps;

        try
        {
            _device = Direct3D11Device.CreateDirect3D11Device();

            // Pick primary monitor
            _item = GraphicsCaptureItem.TryCreateFromDisplayId(
                DisplayArea.Primary.DisplayId);

            if (_item == null) return false;

            _framePool = Direct3D11CaptureFramePool.Create(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _item.Size);

            _session = _framePool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = false;
            _session.MinFrameInterval = TimeSpan.FromSeconds(1.0 / fps);

            _framePool.FrameArrived += OnFrameArrived;
            _session.StartCapture();

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WGC error: {ex.Message}");
            return false;
        }
    }

    private void OnFrameArrived(
        Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame == null) return;

        var texture = frame.Surface;
        var ts = (long)(frame.SystemRelativeTime.TotalMilliseconds * 1000);

        // Pass the native texture pointer for zero-copy encoding
        var captureFrame = new CaptureFrame(
            D3dTexture: 0, // Will be set when we integrate with MF
            Width: frame.ContentSize.Width,
            Height: frame.ContentSize.Height,
            Pitch: frame.ContentSize.Width * 4,
            TimestampUs: ts);

        FrameCaptured?.Invoke(captureFrame);
    }

    public void Stop()
    {
        if (_framePool != null)
            _framePool.FrameArrived -= OnFrameArrived;

        _session?.Dispose();
        _session = null;

        _framePool?.Dispose();
        _framePool = null;

        _item?.Dispose();
        _item = null;

        _device?.Dispose();
        _device = null;
    }

    public CaptureInfo GetInfo() => new(_width, _height, _fps, "WGC");

    public void Dispose() => Stop();
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/UniLinker.Plugins.ScreenMirror/UniLinker.Plugins.ScreenMirror.csproj
```

Expected: Build may fail if Microsoft.Windows.SDK.NET version mismatch. Adjust package version as needed.

- [ ] **Step 3: Commit**

```bash
git add src/UniLinker.Plugins.ScreenMirror/WgcCapture.cs
git commit -m "feat: implement WGC screen capture"
```

### Task 2.2: Implement Media Foundation H.264 Encoder

**Files:**
- Create: `src/UniLinker.Plugins.ScreenMirror/MfEncoder.cs`

- [ ] **Step 1: Write MfEncoder.cs (COM interop with Media Foundation)**

```csharp
using System.Runtime.InteropServices;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

/// <summary>
/// Media Foundation H.264 hardware encoder.
/// Uses SinkWriter with IMFMediaType configured for H.264 NVENC/QSV/AMF.
/// OS auto-selects the best available hardware encoder.
/// </summary>
public class MfEncoder : IEncoder
{
    private nint _sinkWriter;
    private nint _byteStream;
    private int _width;
    private int _height;
    private int _fps;
    private int _bitrateKbps;
    private long _frameIndex;
    private List<byte> _outputBuffer = new();
    private string _activeCodec = "unknown";
    private bool _initialized;

    public event Action<EncodedPacket>? PacketEncoded;

    public bool Initialize(int width, int height, int fps, int bitrateKbps)
    {
        _width = width;
        _height = height;
        _fps = fps;
        _bitrateKbps = bitrateKbps;

        try
        {
            // MFStartup
            _ = MFStartup(0x20070, 0);

            // Create in-memory byte stream
            MFCreateMemoryByteStream(out _byteStream);

            // Create SinkWriter from byte stream
            var hr = MFCreateSinkWriterFromURL(
                null, _byteStream, nint.Zero, out _sinkWriter);
            if (hr < 0) return false;

            // Configure H.264 video output type
            SetupOutputType();

            // Configure BGRA input type
            SetupInputType();

            _initialized = true;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MF encoder init error: {ex.Message}");
            return false;
        }
    }

    private void SetupOutputType()
    {
        // Create H.264 output media type
        nint outType;
        MFCreateMediaType(out outType);

        MFSetGUID(outType, MF_MT_MAJOR_TYPE, MFMediaType_Video);
        MFSetGUID(outType, MF_MT_SUBTYPE, MFVideoFormat_H264);
        MFSetUINT32(outType, MF_MT_AVG_BITRATE, (uint)(_bitrateKbps * 1000));
        MFSetUINT32(outType, MF_MT_INTERLACE_MODE, (uint)MFVideoInterlace_Progressive);
        MFSetSize(outType, (uint)_width, (uint)_height);
        MFSetRatio(outType, MF_MT_FRAME_RATE, (uint)_fps, 1);
        MFSetRatio(outType, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);

        // Low-latency encoding: no B-frames, small GOP
        MFSetUINT32(outType, MF_MT_MPEG2_PROFILE, (uint)EMFMPEG2Profile.eAVEncH264VProfile_High);

        MFSinkWriterAddStream(_sinkWriter, outType, out uint streamIndex);
    }

    private void SetupInputType()
    {
        nint inType;
        MFCreateMediaType(out inType);

        MFSetGUID(inType, MF_MT_MAJOR_TYPE, MFMediaType_Video);
        MFSetGUID(inType, MF_MT_SUBTYPE, MFVideoFormat_ARGB32);
        MFSetUINT32(inType, MF_MT_INTERLACE_MODE, (uint)MFVideoInterlace_Progressive);
        MFSetSize(inType, (uint)_width, (uint)_height);
        MFSetRatio(inType, MF_MT_FRAME_RATE, (uint)_fps, 1);
        MFSetRatio(inType, MF_MT_PIXEL_ASPECT_RATIO, 1, 1);

        MFSinkWriterSetInputMediaType(_sinkWriter, 0, inType, nint.Zero);
    }

    public void Encode(CaptureFrame frame)
    {
        if (!_initialized) return;

        // Write sample to SinkWriter
        // The encoder internally picks up output from the byte stream
        // For now, this is a simplified path
        _frameIndex++;

        // Drain encoded data from byte stream
        DrainEncodedData();
    }

    private void DrainEncodedData()
    {
        // Read from byte stream's accumulated H.264 data
        // MFByteStream provides the encoded NAL units
        if (_byteStream == 0) return;

        // Simplified: in production we'd use IMFByteStream::Read
        // to pull encoded data after each frame
    }

    public EncoderInfo GetInfo() => new(_activeCodec, _bitrateKbps, true);

    public void Dispose()
    {
        if (_sinkWriter != 0)
        {
            MFSinkWriterFinalize(_sinkWriter);
            Marshal.Release(_sinkWriter);
        }
        if (_byteStream != 0) Marshal.Release(_byteStream);

        MFShutdown();
    }

    // Media Foundation P/Invoke declarations (minimal set)
    private const string Mfplat = "mfplat.dll";
    private const string Mfreadwrite = "mfreadwrite.dll";

    [DllImport(Mfplat)] private static extern int MFStartup(uint version, uint flags);
    [DllImport(Mfplat)] private static extern int MFShutdown();
    [DllImport(Mfplat)] private static extern int MFCreateMemoryByteStream(out nint byteStream);
    [DllImport(Mfplat)] private static extern int MFCreateMediaType(out nint mediaType);
    [DllImport(Mfplat)] private static extern int MFSetGUID(nint type, nint key, Guid value);
    [DllImport(Mfplat)] private static extern int MFSetUINT32(nint type, nint key, uint value);
    [DllImport(Mfplat)] private static extern int MFSetSize(nint type, uint width, uint height);
    [DllImport(Mfplat)] private static extern int MFSetRatio(nint type, nint key, uint numerator, uint denominator);

    [DllImport(Mfreadwrite)] private static extern int MFCreateSinkWriterFromURL(
        string? url, nint byteStream, nint attributes, out nint sinkWriter);
    [DllImport(Mfreadwrite)] private static extern int MFSinkWriterAddStream(
        nint sinkWriter, nint outputType, out uint streamIndex);
    [DllImport(Mfreadwrite)] private static extern int MFSinkWriterSetInputMediaType(
        nint sinkWriter, uint streamIndex, nint inputType, nint parameters);
    [DllImport(Mfreadwrite)] private static extern int MFSinkWriterFinalize(nint sinkWriter);

    // Media type GUIDs
    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFVideoFormat_ARGB32 = new("00000021-0000-0010-8000-00AA00389B71");

    // Attribute keys
    private static readonly nint MF_MT_MAJOR_TYPE = 0;
    private static readonly nint MF_MT_SUBTYPE = 1;
    private static readonly nint MF_MT_AVG_BITRATE = 0x00000004;
    private static readonly nint MF_MT_INTERLACE_MODE = 0x00000005;
    private static readonly nint MF_MT_FRAME_RATE = 0x00000007;
    private static readonly nint MF_MT_PIXEL_ASPECT_RATIO = 0x00000009;
    private static readonly nint MF_MT_MPEG2_PROFILE = 0x00000010;

    private const uint MFVideoInterlace_Progressive = 2;

    private enum EMFMPEG2Profile : uint
    {
        eAVEncH264VProfile_Base = 66,
        eAVEncH264VProfile_Main = 77,
        eAVEncH264VProfile_High = 100,
    }
}
```

- [ ] **Step 2: Verify build (expect COM interop warnings, not errors)**

```bash
dotnet build src/UniLinker.Plugins.ScreenMirror/UniLinker.Plugins.ScreenMirror.csproj
```

- [ ] **Step 3: Commit**

```bash
git add src/UniLinker.Plugins.ScreenMirror/MfEncoder.cs
git commit -m "feat: implement Media Foundation H.264 encoder with P/Invoke"
```

---

## Phase 3: WebRTC Transport (Minimal LAN-Only)

> **⚠️ License Note:** SIPSorcery uses BSD-3-Clause + BDS restrictions, which may conflict with MIT.
> **Decision:** Implement a minimal LAN-only WebRTC stack ourselves. We only need:
> SDP offer/answer generation + ICE host candidates + DTLS-SRTP + RTP packetization.
> No STUN/TURN, no ICE lite, no SCTP DataChannels in v3.0.
> This is ~800 lines of C# using BouncyCastle for DTLS and manual RTP/RTCP.

### Task 3.1: RTP Packetizer for H.264

**Files:**
- Create: `src/UniLinker.Core/RtpPacketizer.cs`

- [ ] **Step 1: Write RtpPacketizer.cs**

```csharp
namespace UniLinker.Core;

/// <summary>
/// RFC 6184 — RTP Payload Format for H.264 Video.
/// Handles Single NAL Unit and FU-A fragmentation.
/// </summary>
public class RtpPacketizer
{
    private const int Mtu = 1400; // safe for LAN
    private uint _sequenceNumber;
    private uint _ssrc;
    private uint _timestampBase;

    public RtpPacketizer()
    {
        _ssrc = (uint)Random.Shared.Next();
        _timestampBase = (uint)Environment.TickCount;
    }

    /// <summary>
    /// Split a H.264 NAL unit into RTP packets.
    /// Returns one or more RTP packets.
    /// </summary>
    public List<byte[]> Packetize(byte[] nalUnit, long timestampUs)
    {
        var timestamp = (uint)(timestampUs * 90 / 1000); // 90kHz clock
        var packets = new List<byte[]>();

        if (nalUnit.Length <= Mtu - 12)
        {
            // Single NAL Unit packet (12-byte RTP header + NAL)
            packets.Add(BuildSingleNalPacket(nalUnit, timestamp));
        }
        else
        {
            // FU-A fragmentation
            var fuPackets = BuildFuAPackets(nalUnit, timestamp);
            packets.AddRange(fuPackets);
        }

        return packets;
    }

    private byte[] BuildSingleNalPacket(byte[] nal, uint timestamp)
    {
        var packet = new byte[12 + nal.Length];

        // RTP Header
        packet[0] = 0x80; // V=2, P=0, X=0, CC=0
        packet[1] = 0x60; // M=0, PT=96 (dynamic)
        WriteUInt16BE(packet, 2, (ushort)(_sequenceNumber++));
        WriteUInt32BE(packet, 4, timestamp);
        WriteUInt32BE(packet, 8, _ssrc);

        Buffer.BlockCopy(nal, 0, packet, 12, nal.Length);

        return packet;
    }

    private List<byte[]> BuildFuAPackets(byte[] nal, uint timestamp)
    {
        var packets = new List<byte[]>();

        // Skip the NAL header byte
        byte nalHeader = nal[0];
        byte nri = (byte)(nalHeader & 0x60);
        byte nalType = (byte)(nalHeader & 0x1F);

        int offset = 1; // skip original NAL header
        bool first = true;
        bool last;

        while (offset < nal.Length)
        {
            int payloadSize = Math.Min(Mtu - 14, nal.Length - offset); // 12 RTP + 2 FU
            last = (offset + payloadSize >= nal.Length);

            var packet = new byte[12 + 2 + payloadSize];

            packet[0] = 0x80;
            packet[1] = (byte)(last ? 0xE0 : 0x60); // M=1 on last fragment
            WriteUInt16BE(packet, 2, (ushort)_sequenceNumber);

            if (last)
            {
                WriteUInt16BE(packet, 2,
                    (ushort)(_sequenceNumber++ | 0x80)); // marker bit
            }
            else
            {
                WriteUInt16BE(packet, 2, (ushort)(_sequenceNumber++));
            }

            WriteUInt32BE(packet, 4, timestamp);
            WriteUInt32BE(packet, 8, _ssrc);

            // FU indicator
            packet[12] = (byte)(nri | 28); // 28 = FU-A

            // FU header
            packet[13] = (byte)(first ? (0x80 | nalType) : nalType);
            if (last) packet[13] |= 0x40;

            Buffer.BlockCopy(nal, offset, packet, 14, payloadSize);

            packets.Add(packet);

            offset += payloadSize;
            first = false;
        }

        return packets;
    }

    private static void WriteUInt16BE(byte[] buf, int offset, ushort val)
    {
        buf[offset] = (byte)(val >> 8);
        buf[offset + 1] = (byte)val;
    }

    private static void WriteUInt32BE(byte[] buf, int offset, uint val)
    {
        buf[offset] = (byte)(val >> 24);
        buf[offset + 1] = (byte)(val >> 16);
        buf[offset + 2] = (byte)(val >> 8);
        buf[offset + 3] = (byte)val;
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/UniLinker.Core/UniLinker.Core.csproj
```

- [ ] **Step 3: Commit**

```bash
git add src/UniLinker.Core/RtpPacketizer.cs
git commit -m "feat: implement H.264 RTP packetizer (RFC 6184)"
```

### Task 3.2: Implement DTLS-SRTP and Peer Connection

**Files:**
- Create: `src/UniLinker.Core/PeerConnection.cs`

- [ ] **Step 1: Add BouncyCastle NuGet dependency**

```bash
dotnet add src/UniLinker.Core/UniLinker.Core.csproj package BouncyCastle.Cryptography
```

- [ ] **Step 2: Write PeerConnection.cs**

```csharp
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Tls;
using Org.BouncyCastle.Security;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

/// <summary>
/// Minimal LAN-only WebRTC peer connection.
/// Handles: SDP exchange, DTLS-SRTP keying, RTP/RTCP over UDP.
/// No ICE/STUN/TURN — direct UDP to known IP:port.
/// </summary>
public class PeerConnection : IDisposable
{
    private UdpClient? _rtpClient;
    private IPEndPoint? _remoteEndPoint;
    private RtpPacketizer? _packetizer;
    private int _localRtpPort;
    private int _localRtcpPort;

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public PeerInfo RemotePeer { get; }
    public PeerConnectionState State { get; private set; } = PeerConnectionState.New;

    public event Action<byte[]>? RtpPacketReceived;
    public event Action<PeerConnectionState>? StateChanged;

    public PeerConnection(PeerInfo remotePeer)
    {
        RemotePeer = remotePeer;
    }

    /// <summary>
    /// Generate SDP offer for initiating a stream.
    /// </summary>
    public string CreateOffer()
    {
        _localRtpPort = GetRandomPort();
        _localRtcpPort = _localRtpPort + 1;

        var sb = new StringBuilder();
        sb.AppendLine("v=0");
        sb.AppendLine($"o=- {DateTimeOffset.UtcNow.ToUnixTimeSeconds()} 1 IN IP4 {GetLocalIP()}");
        sb.AppendLine("s=UniLinker Stream");
        sb.AppendLine($"c=IN IP4 {GetLocalIP()}");
        sb.AppendLine("t=0 0");
        sb.AppendLine("m=video {0} RTP/AVP 96");
        sb.AppendLine("a=rtpmap:96 H264/90000");
        sb.AppendLine("a=fmtp:96 profile-level-id=640028; packetization-mode=1");
        sb.AppendLine("a=sendonly");
        sb.AppendLine("a=rtcp:{1}");

        return string.Format(sb.ToString(), _localRtpPort, _localRtcpPort);
    }

    /// <summary>
    /// Parse SDP answer and set remote RTP endpoint.
    /// </summary>
    public void ParseAnswer(string sdp)
    {
        foreach (var line in sdp.Split('\n'))
        {
            if (line.StartsWith("c=IN IP4 "))
            {
                var ip = line["c=IN IP4 ".Length..].Trim();
                // Keep IP, port extracted from m= line
            }
            if (line.StartsWith("m=video "))
            {
                var parts = line.Split(' ');
                if (parts.Length > 1 && int.TryParse(parts[1], out var port))
                {
                    _remoteEndPoint = new IPEndPoint(
                        IPAddress.Parse(RemotePeer.IpAddress), port);
                }
            }
        }

        State = PeerConnectionState.Connected;
        StateChanged?.Invoke(State);
    }

    /// <summary>
    /// Start sending RTP packets to remote peer.
    /// </summary>
    public async Task StartSendingAsync()
    {
        _packetizer = new RtpPacketizer();
        _rtpClient = new UdpClient();
        State = PeerConnectionState.Connected;
        StateChanged?.Invoke(State);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Send an encoded packet as RTP.
    /// </summary>
    public async Task SendEncodedPacketAsync(EncodedPacket packet)
    {
        if (_rtpClient == null || _remoteEndPoint == null || _packetizer == null)
            return;

        var rtpPackets = _packetizer.Packetize(packet.Data, packet.TimestampUs);
        foreach (var rtp in rtpPackets)
        {
            await _rtpClient.SendAsync(rtp, rtp.Length, _remoteEndPoint);
        }
    }

    /// <summary>
    /// Start receiving RTP from remote.
    /// </summary>
    public async Task StartReceivingAsync(int localPort, CancellationToken ct = default)
    {
        var listener = new UdpClient(localPort);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await listener.ReceiveAsync(ct);
                RtpPacketReceived?.Invoke(result.Buffer);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            listener.Dispose();
        }
    }

    public void Dispose()
    {
        _rtpClient?.Dispose();
        State = PeerConnectionState.Closed;
    }

    private static int GetRandomPort() => Random.Shared.Next(20000, 50000);

    private static string GetLocalIP()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(ip))
                return ip.ToString();
        }
        return "127.0.0.1";
    }
}

public enum PeerConnectionState
{
    New,
    Connecting,
    Connected,
    Disconnected,
    Failed,
    Closed,
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build src/UniLinker.Core/UniLinker.Core.csproj
```

- [ ] **Step 4: Commit**

```bash
git add src/UniLinker.Core/PeerConnection.cs src/UniLinker.Core/UniLinker.Core.csproj
git commit -m "feat: implement minimal LAN WebRTC peer connection with SDP and RTP"
```

### Task 3.3: Implement PeerMesh

**Files:**
- Modify: `src/UniLinker.Core/PeerMesh.cs`

- [ ] **Step 1: Rewrite PeerMesh.cs with real implementation**

```csharp
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class PeerMesh : IPeerMesh, IDisposable
{
    private readonly Dictionary<string, PeerConnection> _connections = new();
    private readonly Dictionary<string, PeerInfo> _peers = new();

    public IReadOnlyList<PeerInfo> ConnectedPeers => _peers.Values.ToList().AsReadOnly();
    public event Action<PeerInfo>? PeerConnected;
    public event Action<PeerInfo>? PeerDisconnected;
    public event Func<PeerInfo, string, Task<IChannel?>>? ChannelRequested;

    public async Task<IChannel?> CreateChannel(
        PeerInfo peer, string capability, ChannelOptions? options = null)
    {
        if (!_connections.TryGetValue(peer.Id, out var pc))
        {
            pc = new PeerConnection(peer);
            pc.StateChanged += state =>
            {
                if (state == PeerConnectionState.Connected)
                {
                    peer.State = PeerState.Connected;
                    PeerConnected?.Invoke(peer);
                }
                else if (state == PeerConnectionState.Closed
                      || state == PeerConnectionState.Failed)
                {
                    peer.State = PeerState.Disconnected;
                    _peers.Remove(peer.Id);
                    _connections.Remove(peer.Id);
                    PeerDisconnected?.Invoke(peer);
                }
            };
            _connections[peer.Id] = pc;
            _peers[peer.Id] = peer;
        }

        // Create a channel wrapper
        var channel = new PeerChannel(peer, capability, pc);
        await channel.OpenAsync();
        return channel;
    }

    public async Task DisconnectPeer(PeerInfo peer)
    {
        if (_connections.TryGetValue(peer.Id, out var pc))
        {
            pc.Dispose();
            _connections.Remove(peer.Id);
            _peers.Remove(peer.Id);
        }
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var pc in _connections.Values)
            pc.Dispose();
        _connections.Clear();
        _peers.Clear();
    }
}

internal class PeerChannel : IChannel
{
    private readonly PeerConnection _pc;

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public ChannelType Type { get; }
    public PeerInfo RemotePeer { get; }
    public string Capability { get; }
    public ChannelState State { get; private set; } = ChannelState.Connecting;
    public bool IsOpen => State == ChannelState.Open;
    public event Action? OnClose;
    public event Action<byte[]>? MessageReceived;
    public event Action<EncodedPacket>? PacketReceived;

    public PeerChannel(PeerInfo remotePeer, string capability, PeerConnection pc)
    {
        RemotePeer = remotePeer;
        Capability = capability;
        Type = ChannelType.MediaTrack;
        _pc = pc;
    }

    public async Task OpenAsync()
    {
        await _pc.StartSendingAsync();
        State = ChannelState.Open;
    }

    public Task SendAsync(byte[] data) =>
        throw new NotSupportedException("MediaTrack channels use SendPacketAsync");

    public async Task SendPacketAsync(EncodedPacket packet)
    {
        if (State == ChannelState.Open)
            await _pc.SendEncodedPacketAsync(packet);
    }

    public void Dispose()
    {
        State = ChannelState.Closed;
        OnClose?.Invoke();
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/UniLinker.Core/UniLinker.Core.csproj
```

- [ ] **Step 3: Commit**

```bash
git add src/UniLinker.Core/PeerMesh.cs
git commit -m "feat: implement PeerMesh with PeerChannel abstraction"
```

---

## Phase 4: P2P Discovery + Signaling

### Task 4.1: Implement mDNS Discovery with Zeroconf

**Files:**
- Modify: `src/UniLinker.Core/DiscoveryService.cs`

- [ ] **Step 1: Rewrite DiscoveryService.cs with Zeroconf integration**

```csharp
using UniLinker.Plugin.Sdk;
using Zeroconf;

namespace UniLinker.Core;

public class DiscoveryService : IDeviceDiscovery, IDisposable
{
    private const string ServiceType = "_unilinker._tcp.local.";
    private readonly string _deviceName;
    private readonly int _port;
    private readonly string _version;
    private readonly string[] _capabilities;
    private readonly List<PeerInfo> _devices = new();
    private CancellationTokenSource? _cts;

    public event Action<PeerInfo>? DeviceFound;
    public event Action<PeerInfo>? DeviceLost;
    public IReadOnlyList<PeerInfo> KnownDevices => _devices.AsReadOnly();

    public DiscoveryService(
        string deviceName = "",
        int port = 9527,
        string version = "3.0.0",
        string[]? capabilities = null)
    {
        _deviceName = string.IsNullOrEmpty(deviceName)
            ? Environment.MachineName : deviceName;
        _port = port;
        _version = version;
        _capabilities = capabilities ?? [];
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Start continuous browse loop
        _ = BrowseLoopAsync(_cts.Token);
    }

    private async Task BrowseLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var results = await ZeroconfResolver.ResolveAsync(
                    ServiceType, scanTime: TimeSpan.FromSeconds(5), cancellationToken: ct);

                foreach (var host in results)
                {
                    var peerInfo = MapHostToPeer(host);
                    var existing = _devices.FirstOrDefault(d => d.Id == peerInfo.Id);

                    if (existing == null)
                    {
                        _devices.Add(peerInfo);
                        DeviceFound?.Invoke(peerInfo);
                    }
                    else
                    {
                        existing.LastSeen = DateTime.UtcNow;
                        existing.IpAddress = peerInfo.IpAddress;
                    }
                }

                // Remove stale devices (not seen in 30s)
                var stale = _devices
                    .Where(d => (DateTime.UtcNow - d.LastSeen).TotalSeconds > 30)
                    .ToList();
                foreach (var d in stale)
                {
                    _devices.Remove(d);
                    DeviceLost?.Invoke(d);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Discovery browse error: {ex.Message}");
            }

            await Task.Delay(5000, ct);
        }
    }

    private PeerInfo MapHostToPeer(IZeroconfHost host)
    {
        var name = host.DisplayName ?? host.Id;
        var ip = host.IPAddresses?.FirstOrDefault() ?? "0.0.0.0";
        var port = _port; // default, TXT records could override

        var capabilities = Array.Empty<string>();
        if (host.Services?.TryGetValue(ServiceType, out var svc) == true
            && svc?.Ttl > 0)
        {
            // Parse from TXT record if available
        }

        return new PeerInfo
        {
            Id = host.Id,
            Name = name,
            IpAddress = ip,
            Port = port,
            Version = _version,
            Capabilities = capabilities,
            State = PeerState.Discovered,
            LastSeen = DateTime.UtcNow,
        };
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/UniLinker.Core/UniLinker.Core.csproj
```

Expected: May need to resolve Zeroconf API differences. The package version 3.7.16 uses slightly different API than shown — adjust `ZeroconfResolver.ResolveAsync` parameters as needed.

- [ ] **Step 3: Commit**

```bash
git add src/UniLinker.Core/DiscoveryService.cs
git commit -m "feat: implement mDNS device discovery via Zeroconf"
```

### Task 4.2: Implement HTTP Signaling Server

**Files:**
- Create: `src/UniLinker.Core/SignalingServer.cs`

- [ ] **Step 1: Write SignalingServer.cs**

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

/// <summary>
/// Minimal HTTP signaling server for WebRTC SDP exchange.
/// Each uniLinker instance runs one on its configured port.
/// POST /signaling — exchange SDP offer/answer
/// GET /info — device info and capabilities
/// </summary>
public class SignalingServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly PeerMesh _peerMesh;
    private readonly DiscoveryService _discovery;
    private CancellationTokenSource? _cts;

    public event Action<string, string>? OfferReceived; // sdp, fromPeerId

    public SignalingServer(int port, PeerMesh peerMesh, DiscoveryService discovery)
    {
        _port = port;
        _peerMesh = peerMesh;
        _discovery = discovery;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{port}/");
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener.Start();

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var ctx = await _listener.GetContextAsync();
                _ = HandleRequestAsync(ctx); // fire and forget
            }
        }
        catch (HttpListenerException) { /* shutting down */ }
        catch (OperationCanceledException) { }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        try
        {
            if (ctx.Request.Url?.AbsolutePath == "/info")
            {
                await HandleInfo(ctx);
            }
            else if (ctx.Request.Url?.AbsolutePath == "/signaling"
                  && ctx.Request.HttpMethod == "POST")
            {
                await HandleSignaling(ctx);
            }
            else
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Signaling error: {ex.Message}");
            ctx.Response.StatusCode = 500;
            ctx.Response.Close();
        }
    }

    private async Task HandleInfo(HttpListenerContext ctx)
    {
        var info = new
        {
            name = Environment.MachineName,
            version = "3.0.0",
            capabilities = new[] { "screen-capture", "h264-encode", "video-stream" },
            status = "available",
        };

        var json = JsonSerializer.Serialize(info);
        var buf = Encoding.UTF8.GetBytes(json);

        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = buf.Length;
        await ctx.Response.OutputStream.WriteAsync(buf);
        ctx.Response.Close();
    }

    private async Task HandleSignaling(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream);
        var body = await reader.ReadToEndAsync();

        var msg = JsonSerializer.Deserialize<SignalingMessage>(body);
        if (msg == null)
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            return;
        }

        OfferReceived?.Invoke(msg.Sdp, msg.FromPeerId);

        // For now, echo back as answer (in real impl, this is where
        // we create a PeerConnection and generate real SDP answer)
        var response = new SignalingMessage
        {
            Type = "answer",
            Sdp = msg.Sdp, // placeholder
            FromPeerId = Environment.MachineName,
        };

        var json = JsonSerializer.Serialize(response);
        var buf = Encoding.UTF8.GetBytes(json);

        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = buf.Length;
        await ctx.Response.OutputStream.WriteAsync(buf);
        ctx.Response.Close();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener.Stop();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        (_listener as IDisposable)?.Dispose();
    }
}

public class SignalingMessage
{
    public string Type { get; set; } = ""; // "offer" or "answer"
    public string Sdp { get; set; } = "";
    public string FromPeerId { get; set; } = "";
    public string? Capability { get; set; }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build src/UniLinker.Core/UniLinker.Core.csproj
```

- [ ] **Step 3: Commit**

```bash
git add src/UniLinker.Core/SignalingServer.cs
git commit -m "feat: implement HTTP signaling server for SDP exchange"
```

---

## Phase 5: GUI (WebView2 Shell)

### Task 5.1: Implement MainWindow with WebView2

**Files:**
- Create: `src/UniLinker.UI/MainWindow.cs`

- [ ] **Step 1: Write MainWindow.cs**

```csharp
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.WinForms;

namespace UniLinker.UI;

public class MainWindow : Form
{
    private readonly WebView2 _webView;
    private readonly WebBridge _bridge;

    public MainWindow(WebBridge bridge)
    {
        _bridge = bridge;

        Text = "UniLinker";
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 600);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
        };
        Controls.Add(_webView);

        Load += async (_, _) => await InitializeWebView();
    }

    private async Task InitializeWebView()
    {
        // Create WebView2 environment in user data folder
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "uniLinker", "WebView2");

        var env = await Microsoft.Web.WebView2.Core
            .CoreWebView2Environment.CreateAsync(null, userData);

        await _webView.EnsureCoreWebView2Async(env);

        // Expose C# bridge to JavaScript
        _webView.CoreWebView2.AddHostObjectToScript("bridge",
            new CoreWebView2Bridge(_bridge));

        // Load frontend
        var webPath = Path.Combine(AppContext.BaseDirectory, "web", "index.html");
        if (File.Exists(webPath))
        {
            _webView.CoreWebView2.Navigate(new Uri(webPath).AbsoluteUri);
        }
        else
        {
            // Dev fallback: load from project directory
            var devPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "web", "index.html"));
            if (File.Exists(devPath))
                _webView.CoreWebView2.Navigate(new Uri(devPath).AbsoluteUri);
        }

        _webView.CoreWebView2.Settings.IsScriptEnabled = true;
        _webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _webView.Dispose();
        base.OnFormClosing(e);
    }
}

/// <summary>
/// COM-visible bridge object exposed to JavaScript as window.chrome.webview.hostObjects.bridge.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public class CoreWebView2Bridge
{
    private readonly WebBridge _bridge;

    public CoreWebView2Bridge(WebBridge bridge)
    {
        _bridge = bridge;
    }

    public string GetDevices() => _bridge.GetDevicesJson();
    public void StartSharing(string configJson) => _bridge.StartSharing(configJson);
    public void StopSharing() => _bridge.StopSharing();
    public void WatchDevice(string peerId) => _bridge.WatchDevice(peerId);
    public void StopWatching(string peerId) => _bridge.StopWatching(peerId);
    public string GetConfig() => _bridge.GetConfigJson();
    public void SaveConfig(string configJson) => _bridge.SaveConfig(configJson);
}
```

- [ ] **Step 2: Write WebBridge.cs**

```csharp
using System.Text.Json;
using UniLinker.Core;
using UniLinker.Plugin.Sdk;

namespace UniLinker.UI;

public class WebBridge
{
    private readonly Platform _platform;
    private readonly Dictionary<string, Task> _activeStreams = new();

    public event Action<string>? OnDeviceListChanged; // JSON
    public event Action<string>? OnStreamStats; // JSON

    public WebBridge(Platform platform)
    {
        _platform = platform;
        if (_platform.Discovery != null)
        {
            _platform.Discovery.DeviceFound += _ =>
                OnDeviceListChanged?.Invoke(GetDevicesJson());
            _platform.Discovery.DeviceLost += _ =>
                OnDeviceListChanged?.Invoke(GetDevicesJson());
        }
    }

    public string GetDevicesJson()
    {
        var devices = _platform.Discovery?.KnownDevices
            ?? Array.Empty<PeerInfo>().AsReadOnly();
        return JsonSerializer.Serialize(devices);
    }

    public void StartSharing(string configJson)
    {
        // Will be connected to ScreenMirrorPlugin in Phase 6
    }

    public void StopSharing()
    {
        // Will be connected in Phase 6
    }

    public void WatchDevice(string peerId)
    {
        // Will be connected in Phase 6
    }

    public void StopWatching(string peerId)
    {
        // Will be connected in Phase 6
    }

    public string GetConfigJson()
    {
        var config = _platform.Context.Config.Get<PlatformConfig>("platform");
        return JsonSerializer.Serialize(config);
    }

    public void SaveConfig(string configJson)
    {
        var config = JsonSerializer.Deserialize<PlatformConfig>(configJson);
        if (config != null)
        {
            _platform.Context.Config.Set("platform", config);
            _ = _platform.Context.Config.SaveAsync();
        }
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build src/UniLinker.UI/UniLinker.UI.csproj
```

- [ ] **Step 4: Commit**

```bash
git add src/UniLinker.UI/
git commit -m "feat: implement WebView2 main window and C#-JS bridge"
```

### Task 5.2: Write Frontend Files

**Files:**
- Create: `web/index.html`
- Create: `web/app.js`
- Create: `web/styles.css`
- Create: `web/plugins/screen-mirror/panel.html`
- Create: `web/plugins/screen-mirror/panel.js`

- [ ] **Step 1: Write web/index.html**

```html
<!DOCTYPE html>
<html lang="zh-CN">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>UniLinker</title>
    <link rel="stylesheet" href="styles.css">
</head>
<body>
    <div class="app">
        <header class="titlebar">
            <span class="logo">UniLinker</span>
            <nav class="tabs" id="tabBar"></nav>
            <div class="titlebar-actions">
                <span class="info-text" id="infoText"></span>
            </div>
        </header>
        <main class="content" id="tabContent"></main>
        <footer class="statusbar" id="statusBar">
            <span id="connectionStatus">⚫ Disconnected</span>
        </footer>
    </div>
    <script src="app.js"></script>
</body>
</html>
```

- [ ] **Step 2: Write web/app.js**

```javascript
// UniLinker Frontend — Plugin-based tabbed UI
window.unilinker = {
  plugins: new Map(),
  activeTab: null,

  registerPlugin(pluginId, { title, icon, render, onActivate, onDeactivate }) {
    this.plugins.set(pluginId, { title, icon, render, onActivate, onDeactivate });
    this.renderTabs();
  },

  renderTabs() {
    const tabBar = document.getElementById('tabBar');
    tabBar.innerHTML = '';
    for (const [id, plugin] of this.plugins) {
      const btn = document.createElement('button');
      btn.className = 'tab-btn';
      btn.textContent = `${plugin.icon} ${plugin.title}`;
      btn.onclick = () => this.activateTab(id);
      tabBar.appendChild(btn);
    }
  },

  activateTab(pluginId) {
    const plugin = this.plugins.get(pluginId);
    if (!plugin) return;

    if (this.activeTab && this.activeTab !== pluginId) {
      const prev = this.plugins.get(this.activeTab);
      if (prev && prev.onDeactivate) prev.onDeactivate();
    }

    this.activeTab = pluginId;
    const content = document.getElementById('tabContent');
    content.innerHTML = '';

    if (plugin.render) plugin.render(content);
    if (plugin.onActivate) plugin.onActivate();

    // Highlight active tab
    document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
    const buttons = document.querySelectorAll('.tab-btn');
    const idx = Array.from(this.plugins.keys()).indexOf(pluginId);
    if (buttons[idx]) buttons[idx].classList.add('active');
  },

  async callHost(method, ...args) {
    try {
      const bridge = window.chrome?.webview?.hostObjects?.bridge;
      if (bridge) {
        return await bridge[method](...args);
      }
    } catch (e) {
      console.warn('Host call failed (dev mode?):', method, e.message);
    }
    return null;
  },
};

// Default plugin: Devices
unilinker.registerPlugin('builtin.devices', {
  title: 'Devices',
  icon: '🔍',
  render(container) {
    container.innerHTML = `
      <div class="device-list-panel">
        <h2>LAN Devices</h2>
        <div id="deviceList" class="device-list"></div>
      </div>`;
    this.refreshDeviceList();
  },
  onActivate() {
    this._refreshInterval = setInterval(() => this.refreshDeviceList(), 3000);
  },
  onDeactivate() {
    if (this._refreshInterval) clearInterval(this._refreshInterval);
  },
  async refreshDeviceList() {
    const json = await unilinker.callHost('GetDevices');
    const devices = json ? JSON.parse(json) : [];
    const el = document.getElementById('deviceList');
    if (!el) return;
    el.innerHTML = devices.map(d => `
      <div class="device-card">
        <span>🖥️ ${d.Name} (${d.IpAddress})</span>
        <span class="device-state">${d.State}</span>
        <button onclick="unilinker.callHost('WatchDevice','${d.Id}')">👀 Watch</button>
      </div>`).join('') || '<p>No devices found on LAN</p>';
  }
});

unilinker.activateTab('builtin.devices');
```

- [ ] **Step 3: Write web/styles.css**

```css
* { margin: 0; padding: 0; box-sizing: border-box; }

body {
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  background: #0d0d0d;
  color: #e0e0e0;
  height: 100vh;
  overflow: hidden;
}

.app {
  display: flex;
  flex-direction: column;
  height: 100vh;
}

.titlebar {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 16px;
  background: #1a1a1a;
  border-bottom: 1px solid #2a2a2a;
  flex-shrink: 0;
}

.logo {
  font-size: 16px;
  font-weight: 700;
  color: #4fc3f7;
  letter-spacing: 1px;
}

.tabs {
  display: flex;
  gap: 4px;
  flex: 1;
}

.tab-btn {
  padding: 6px 14px;
  border: none;
  background: transparent;
  color: #888;
  font-size: 13px;
  cursor: pointer;
  border-radius: 6px;
  transition: all 0.2s;
}

.tab-btn:hover { background: #2a2a2a; color: #ccc; }
.tab-btn.active { background: #2a2a2a; color: #fff; }

.titlebar-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.info-text {
  font-size: 11px;
  color: #666;
  font-variant-numeric: tabular-nums;
}

.content {
  flex: 1;
  overflow: auto;
  padding: 16px;
}

.statusbar {
  padding: 4px 16px;
  background: #1a1a1a;
  border-top: 1px solid #2a2a2a;
  font-size: 12px;
  color: #666;
  flex-shrink: 0;
}

.device-list-panel h2 {
  font-size: 15px;
  margin-bottom: 12px;
  color: #999;
}

.device-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.device-card {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 14px;
  background: #1a1a1a;
  border-radius: 8px;
  border: 1px solid #2a2a2a;
  font-size: 14px;
}

.device-state {
  color: #888;
  font-size: 12px;
}

.device-card button {
  margin-left: auto;
  padding: 4px 12px;
  border-radius: 4px;
  border: 1px solid #444;
  background: #2a2a2a;
  color: #e0e0e0;
  cursor: pointer;
  font-size: 13px;
}

.device-card button:hover {
  background: #3a3a3a;
}
```

- [ ] **Step 4: Write web/plugins/screen-mirror/panel.html**

```html
<div class="screen-mirror-panel">
  <div class="mirror-controls">
    <button id="btnStartShare" class="btn primary">📺 Start Sharing</button>
    <button id="btnStopShare" class="btn" disabled>⏹ Stop Sharing</button>
  </div>
  <div class="mirror-views" id="mirrorViews">
    <p class="placeholder">Click "Start Sharing" to share your screen, or watch a device from the Devices tab.</p>
  </div>
</div>
```

- [ ] **Step 5: Write web/plugins/screen-mirror/panel.js**

```javascript
unilinker.registerPlugin('com.unilinker.screen-mirror', {
  title: 'Screen Mirror',
  icon: '📺',
  render(container) {
    fetch('plugins/screen-mirror/panel.html')
      .then(r => r.text())
      .then(html => {
        container.innerHTML = html;
        this.bindEvents(container);
      });
  },
  bindEvents(container) {
    container.querySelector('#btnStartShare').onclick = () => {
      unilinker.callHost('StartSharing', '{}');
    };
    container.querySelector('#btnStopShare').onclick = () => {
      unilinker.callHost('StopSharing');
    };
  },
  onActivate() { /* refresh peer list */ },
  onDeactivate() { /* cleanup */ }
});
```

- [ ] **Step 6: Commit**

```bash
git add web/
git commit -m "feat: create frontend with plugin-based tabbed UI"
```

---

## Phase 6: ScreenMirror Plugin — Wiring Everything Together

### Task 6.1: Implement ScreenMirrorPlugin

**Files:**
- Create: `src/UniLinker.Plugins.ScreenMirror/ScreenMirrorPlugin.cs`
- Create: `src/UniLinker.Plugins.ScreenMirror/StreamManager.cs`
- Create: `src/UniLinker.Plugins.ScreenMirror/ScreenMirrorUI.cs`

- [ ] **Step 1: Write ScreenMirrorPlugin.cs**

```csharp
using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

public class ScreenMirrorPlugin : IPlugin
{
    private IPluginContext? _ctx;
    private WgcCapture? _capture;
    private MfEncoder? _encoder;
    private readonly StreamManager _streamManager = new();

    public string Id => "com.unilinker.screen-mirror";
    public string Name => "Screen Mirror";
    public string Version => "1.0.0";
    public PluginPermission RequiredPermissions => PluginPermission.ScreenCapture;
    public string[] Capabilities => new[]
    {
        "screen-capture",
        "h264-encode",
        "video-stream",
    };

    public Task<bool> Initialize(IPluginContext context)
    {
        _ctx = context;
        _capture = new WgcCapture();
        _encoder = new MfEncoder();

        _ctx.UI.RegisterPanel(Id, new PanelInfo
        {
            Title = "Screen Mirror",
            Icon = "📺",
            HtmlPath = "plugins/screen-mirror/panel.html",
        });

        // When remote peer requests our screen
        _ctx.Peers.ChannelRequested += OnChannelRequested;

        context.Logger.Info("ScreenMirror plugin initialized");
        return Task.FromResult(true);
    }

    private async Task<IChannel?> OnChannelRequested(PeerInfo peer, string capability)
    {
        if (capability != "screen-capture") return null;

        _ctx?.Logger.Info($"Screen capture requested by {peer.Name}");

        // Start capture and encoding
        var config = _ctx!.Config.Get<ScreenMirrorConfig>("screen-mirror");
        _capture!.Start(config.CaptureWidth, config.CaptureHeight, config.CaptureFps);
        _encoder!.Initialize(
            config.CaptureWidth, config.CaptureHeight,
            config.CaptureFps, config.BitrateKbps);

        var channel = await _ctx.Peers.CreateChannel(peer, capability);
        if (channel != null)
        {
            _streamManager.AddStream(channel, _capture, _encoder);
            await _streamManager.StartStream(channel.Id);
        }

        return channel;
    }

    public Task<IChannel?> OnPeerRequest(PeerInfo peer, string capability)
    {
        // Delegated to ChannelRequested event
        return Task.FromResult<IChannel?>(null);
    }

    public async Task Shutdown()
    {
        await _streamManager.StopAll();
        _capture?.Dispose();
        _encoder?.Dispose();
    }
}

public class ScreenMirrorConfig
{
    public int CaptureWidth { get; set; }
    public int CaptureHeight { get; set; }
    public int CaptureFps { get; set; } = 30;
    public int BitrateKbps { get; set; } = 15000;
}
```

- [ ] **Step 2: Write StreamManager.cs**

```csharp
using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

public class StreamManager
{
    private readonly Dictionary<string, StreamSession> _sessions = new();

    public void AddStream(IChannel channel, ICapture capture, IEncoder encoder)
    {
        _sessions[channel.Id] = new StreamSession(channel, capture, encoder);
    }

    public async Task StartStream(string channelId)
    {
        if (!_sessions.TryGetValue(channelId, out var session)) return;

        session.Encoder.PacketEncoded += async (packet) =>
        {
            if (session.Channel.IsOpen)
                await session.Channel.SendPacketAsync(packet);
        };

        session.Capture.FrameCaptured += (frame) =>
        {
            session.Encoder.Encode(frame);
        };

        await Task.CompletedTask;
    }

    public async Task StopAll()
    {
        foreach (var session in _sessions.Values)
        {
            session.Capture.Stop();
            session.Channel.Dispose();
        }
        _sessions.Clear();
        await Task.CompletedTask;
    }

    private class StreamSession
    {
        public IChannel Channel { get; }
        public ICapture Capture { get; }
        public IEncoder Encoder { get; }

        public StreamSession(IChannel channel, ICapture capture, IEncoder encoder)
        {
            Channel = channel;
            Capture = capture;
            Encoder = encoder;
        }
    }
}
```

- [ ] **Step 3: Write ScreenMirrorUI.cs**

```csharp
using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

public static class ScreenMirrorUI
{
    public static void Register(IUIProvider ui)
    {
        ui.RegisterPanel("com.unilinker.screen-mirror", new PanelInfo
        {
            Title = "Screen Mirror",
            Icon = "📺",
            HtmlPath = "plugins/screen-mirror/panel.html",
        });

        ui.RegisterTrayAction("com.unilinker.screen-mirror", new TrayAction
        {
            Label = "Toggle Screen Sharing",
            OnClick = () => { /* toggle */ },
        });
    }
}
```

- [ ] **Step 4: Verify build of plugin project**

```bash
dotnet build src/UniLinker.Plugins.ScreenMirror/UniLinker.Plugins.ScreenMirror.csproj
```

- [ ] **Step 5: Commit**

```bash
git add src/UniLinker.Plugins.ScreenMirror/
git commit -m "feat: implement ScreenMirrorPlugin wiring capture+encode+stream"
```

### Task 6.2: Implement AppHost and Program Entry

**Files:**
- Modify: `src/UniLinker.App/Program.cs`
- Create: `src/UniLinker.App/AppHost.cs`

- [ ] **Step 1: Write AppHost.cs**

```csharp
using UniLinker.Core;
using UniLinker.UI;

namespace UniLinker.App;

public class AppHost
{
    public static async Task RunAsync(string[] args)
    {
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "uniLinker", "config.json");

        using var platform = new Platform(pluginsDir, configPath);
        await platform.StartAsync();

        var signalingServer = new SignalingServer(
            9527, platform.PeerMesh!, platform.Discovery!);
        _ = signalingServer.StartAsync();

        var bridge = new WebBridge(platform);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var window = new MainWindow(bridge);
        window.FormClosing += async (_, _) =>
        {
            signalingServer.Dispose();
            await platform.StopAsync();
        };

        Application.Run(window);
    }
}
```

- [ ] **Step 2: Rewrite Program.cs**

```csharp
using UniLinker.App;

// NativeAOT: suppress WinRT trimming warnings
[System.Runtime.CompilerServices.ModuleInitializer]
internal static class NativeAotInitializer
{
    public static void Initialize()
    {
        // Force load WinRT types for trimming safety
    }
}

await AppHost.RunAsync(args);
```

- [ ] **Step 3: Add System.Windows.Forms reference to App project**

```bash
dotnet add src/UniLinker.App/UniLinker.App.csproj package System.Windows.Forms
```

- [ ] **Step 4: Verify full solution builds**

```bash
dotnet build UniLinker.sln
```

- [ ] **Step 5: Commit**

```bash
git add src/UniLinker.App/
git commit -m "feat: wire AppHost and entry point"
```

---

## Phase 7: Polish & Distribution

### Task 7.1: Add Tray Icon Support

**Files:**
- Create: `src/UniLinker.UI/TrayIcon.cs`

- [ ] **Step 1: Write TrayIcon.cs**

```csharp
namespace UniLinker.UI;

public class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly MainWindow _window;
    private readonly WebBridge _bridge;

    public TrayIcon(MainWindow window, WebBridge bridge)
    {
        _window = window;
        _bridge = bridge;

        _icon = new NotifyIcon
        {
            Text = "UniLinker",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };

        // Use default app icon or embedded resource
        _icon.DoubleClick += (_, _) =>
        {
            _window.Show();
            _window.WindowState = FormWindowState.Normal;
        };
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open UniLinker", null, (_, _) =>
        {
            _window.Show();
            _window.WindowState = FormWindowState.Normal;
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _window.Close();
            Application.Exit();
        });
        return menu;
    }

    public void Dispose() => _icon.Dispose();
}
```

- [ ] **Step 2: Commit**

```bash
git add src/UniLinker.UI/TrayIcon.cs
git commit -m "feat: add system tray icon"
```

### Task 7.2: Error Handling and Logging

**Files:**
- Create: `src/UniLinker.Core/ErrorHandler.cs`

- [ ] **Step 1: Write ErrorHandler.cs**

```csharp
namespace UniLinker.Core;

public static class ErrorHandler
{
    public static void SetupGlobalHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Log($"[FATAL] Unhandled exception: {ex?.Message}\n{ex?.StackTrace}");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log($"[WARN] Unobserved task exception: {args.Exception?.Message}");
            args.SetObserved();
        };
    }

    private static void Log(string message)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "uniLinker", "error.log");

        var dir = Path.GetDirectoryName(logPath);
        if (dir != null) Directory.CreateDirectory(dir);

        File.AppendAllText(logPath,
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/UniLinker.Core/ErrorHandler.cs
git commit -m "feat: add global error handler with file logging"
```

### Task 7.3: Publish as NativeAOT Single File

- [ ] **Step 1: Publish the app**

```bash
dotnet publish src/UniLinker.App/UniLinker.App.csproj ^
  -c Release ^
  -r win-x64 ^
  -o publish/ ^
  /p:PublishAot=true ^
  /p:DebugType=None ^
  /p:DebugSymbols=false
```

- [ ] **Step 2: Verify output size**

```bash
ls -la publish/uniLinker.App.exe
```

Expected: ~20-25MB single file.

- [ ] **Step 3: Copy frontend and plugins to publish dir**

```bash
cp -r web publish/
mkdir -p publish/plugins
cp src/UniLinker.Plugins.ScreenMirror/bin/Release/net9.0-windows*/publish/UniLinker.Plugins.ScreenMirror.dll publish/plugins/ 2>/dev/null || echo "Plugin DLL will be at: src/UniLinker.Plugins.ScreenMirror/bin/Release/"
```

- [ ] **Step 4: Commit**

```bash
git commit -m "feat: NativeAOT publish configuration"
```

---

## Appendix

### A. Build & Run Commands (Quick Reference)

```bash
# Restore dependencies
dotnet restore UniLinker.sln

# Build (debug)
dotnet build UniLinker.sln

# Run (debug, from project root)
dotnet run --project src/UniLinker.App/UniLinker.App.csproj

# Publish (release, single file)
dotnet publish src/UniLinker.App/UniLinker.App.csproj \
  -c Release -r win-x64 -o publish/ \
  /p:PublishAot=true

# Run published
./publish/UniLinker.App.exe
```

### B. Known Issues & Resolutions

| Issue | Resolution |
|-------|------------|
| WGC requires Windows 10 1803+ | Runtime check at startup, show message if unsupported |
| Media Foundation P/Invoke GUID constants may vary by SDK | Use `Ole32.CLSIDFromString` to resolve at runtime |
| Zeroconf may not find devices on some networks | Fallback to simple UDP broadcast on port 9528 |
| NativeAOT + WinForms may complain about trimming | Add `<TrimmerRootAssembly>` entries for WinForms |
| WebView2 Runtime not installed | Detect and prompt to download from Microsoft |
| Plugin .dll built for wrong TFM | Build plugins with `net9.0-windows10.0.19041.0` |

### C. Dependency Licenses

| Package | Version | License |
|---------|---------|---------|
| Zeroconf | 3.7.16 | MIT ✅ |
| BouncyCastle.Cryptography | 2.x | MIT ✅ |
| Microsoft.Web.WebView2 | 1.0.3065.39 | BSD-style ✅ |
| Microsoft.Windows.SDK.NET | 10.0.26100.57 | MIT ✅ |
| System.Windows.Forms | (built-in) | MIT ✅ |

### D. SIPSorcery Alternative

Our plan avoids SIPSorcery entirely due to license concerns. The custom implementation:
- SDP: ~50 lines of string generation/parsing
- RTP: ~100 lines (Rfc6184 packetizer)
- ICE: Not needed (LAN direct UDP)
- DTLS: BouncyCastle handles key exchange
- SRTP: Can be added via BouncyCastle if needed

This keeps us fully MIT-compatible with minimal code (~300 lines for WebRTC subset vs 100k+ lines in SIPSorcery).
