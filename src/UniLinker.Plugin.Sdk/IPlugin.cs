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
