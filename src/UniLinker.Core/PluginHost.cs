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
            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Plugin '{loaded.Plugin.Id}' init failed: {ex.Message}");
            }
        }
    }

    public async Task ShutdownAllAsync()
    {
        foreach (var loaded in _loader.LoadedPlugins)
        {
            try { await loaded.Plugin.Shutdown(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Plugin '{loaded.Plugin.Id}' shutdown error: {ex.Message}");
            }
            try { loaded.Context.Unload(); }
            catch { }
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
