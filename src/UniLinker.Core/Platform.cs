using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class Platform : IDisposable
{
    private readonly PluginLoader _pluginLoader;
    private readonly ConfigStore _configStore;
    private readonly string _pluginsDir;

    public IPluginContext Context { get; }
    public PeerMesh PeerMesh { get; } = new();
    public DiscoveryService Discovery { get; private set; } = null!;
    public PluginHost PluginHost { get; }

    public Platform(string pluginsDir, string configPath)
    {
        _pluginsDir = pluginsDir;
        _configStore = new ConfigStore(configPath);
        _pluginLoader = new PluginLoader();

        Context = new PlatformContext
        {
            Peers = PeerMesh,
            Discovery = null!,
            Config = _configStore,
            Logger = new ConsoleLogger(),
            UI = new NullUIProvider(),
        };

        PluginHost = new PluginHost(_pluginLoader, Context);

        // Route data channels opened by remote peers to the owning plugin.
        PeerMesh.DataChannelReady += (peer, channel) => _ = OnIncomingChannel(peer, channel);
    }

    /// <summary>
    /// Register a built-in plugin instance (compiled into the app) to be
    /// initialized on StartAsync. External plugins are still discovered from
    /// the plugins directory.
    /// </summary>
    public void RegisterPlugin(IPlugin plugin)
    {
        PluginHost.Register(plugin);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _configStore.LoadAsync();

        var config = _configStore.Get<PlatformConfig>("platform");

        // Discovery must be created after config so the advertised device
        // name and port match the configured values.
        Discovery = new DiscoveryService(
            config.DeviceName, config.HttpPort > 0 ? config.HttpPort : 9527);
        if (Context is PlatformContext platformContext)
        {
            platformContext.Discovery = Discovery;
        }

        _pluginLoader.DiscoverAndLoad(_pluginsDir);

        await PluginHost.InitializeAllAsync();

        await Discovery.StartAsync(ct);
    }

    private async Task OnIncomingChannel(PeerInfo peer, IChannel channel)
    {
        foreach (var plugin in PluginHost.GetAllPlugins().OfType<IIncomingChannelHandler>())
        {
            try
            {
                await plugin.OnIncomingChannel(peer, channel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Platform] {((IPlugin)plugin).Id} failed to handle incoming channel: {ex.Message}");
            }
        }
    }

    public async Task StopAsync()
    {
        await PluginHost.ShutdownAllAsync();
        Discovery?.StopAsync();
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
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "uniLinker", "unilinker.log");

    public void Debug(string msg) => Write("DBG", msg);
    public void Info(string msg) => Write("INF", msg);
    public void Warn(string msg) => Write("WRN", msg);
    public void Error(string msg, Exception? ex) => Write("ERR", $"{msg} {ex}");

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}";
        Console.WriteLine(line);
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            File.AppendAllText(LogFile, line + Environment.NewLine);
        }
        catch { }
    }
}

internal class NullUIProvider : IUIProvider
{
    public void RegisterPanel(string id, PanelInfo panel) { }
    public void RegisterTrayAction(string id, TrayAction action) { }
}
