using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class PluginHost
{
    private readonly PluginLoader _loader;
    private readonly IPluginContext _contextTemplate;

    public List<LoadedPlugin> Plugins => _loader.LoadedPlugins;

    private readonly List<IPlugin> _builtinPlugins = new();

    public PluginHost(PluginLoader loader, IPluginContext contextTemplate)
    {
        _loader = loader;
        _contextTemplate = contextTemplate;
    }

    /// <summary>
    /// All registered plugins: built-ins first, then directory-loaded ones.
    /// </summary>
    public IEnumerable<IPlugin> GetAllPlugins()
    {
        foreach (var plugin in _builtinPlugins)
            yield return plugin;
        foreach (var loaded in _loader.LoadedPlugins)
            yield return loaded.Plugin;
    }

    /// <summary>
    /// Register a built-in plugin instance (no assembly loaded from disk).
    /// </summary>
    public void Register(IPlugin plugin)
    {
        _builtinPlugins.Add(plugin);
    }

    public async Task InitializeAllAsync()
    {
        foreach (var plugin in _builtinPlugins)
        {
            await InitializePluginAsync(plugin, Path.Combine(
                AppContext.BaseDirectory, $"{plugin.Id}.builtin"));
        }

        foreach (var loaded in _loader.LoadedPlugins)
        {
            await InitializePluginAsync(loaded.Plugin, loaded.AssemblyPath);
        }
    }

    private async Task InitializePluginAsync(IPlugin plugin, string assemblyPath)
    {
        var ctx = new PluginContextWrapper(_contextTemplate, new PluginInfo
        {
            Id = plugin.Id,
            Name = plugin.Name,
            Version = plugin.Version,
            AssemblyPath = assemblyPath,
            Capabilities = plugin.Capabilities,
        });

        var ok = await plugin.Initialize(ctx);
        System.Diagnostics.Debug.WriteLine(
            $"Plugin '{plugin.Id}' initialized: {ok}");
    }

    public async Task ShutdownAllAsync()
    {
        foreach (var plugin in _builtinPlugins)
        {
            try { await plugin.Shutdown(); }
            catch { }
        }
        _builtinPlugins.Clear();

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
