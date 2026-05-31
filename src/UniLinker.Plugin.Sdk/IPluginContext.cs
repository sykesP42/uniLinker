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
