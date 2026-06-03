using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class Platform : IDisposable
{
    private readonly PluginLoader _pluginLoader;
    private readonly PluginHost _pluginHost;
    private readonly ConfigStore _configStore;
    private readonly string _pluginsDir;

    public IPluginContext Context { get; }
    public PeerMesh PeerMesh { get; } = new();
    public DiscoveryService Discovery { get; } = new();

    public Platform(string pluginsDir, string configPath)
    {
        _pluginsDir = pluginsDir;
        _configStore = new ConfigStore(configPath);
        _pluginLoader = new PluginLoader();

        Context = new PlatformContext
        {
            Peers = PeerMesh,
            Discovery = Discovery,
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

        _pluginLoader.DiscoverAndLoad(_pluginsDir);
        await _pluginHost.InitializeAllAsync();

        await Discovery.StartAsync(ct);
    }

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
