namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Core plugin interface that all UniLinker plugins must implement.
/// Plugins are loaded from DLLs in the plugins/ directory by PluginHost.
/// </summary>
public interface IPlugin
{
    /// <summary>Unique identifier for this plugin (e.g., "screen-mirror", "file-transfer").</summary>
    string Id { get; }

    /// <summary>Human-readable display name.</summary>
    string Name { get; }

    /// <summary>Semantic version string (e.g., "1.0.0").</summary>
    string Version { get; }

    /// <summary>Permissions required by this plugin (combined via bitwise OR).</summary>
    PluginPermission RequiredPermissions { get; }

    /// <summary>
    /// Capabilities this plugin provides (e.g., "screen-capture", "h264-encode").
    /// Used for matching peer requests to the appropriate plugin.
    /// </summary>
    string[] Capabilities { get; }

    /// <summary>
    /// Initialize the plugin with access to platform services.
    /// Called once when the plugin is loaded.
    /// </summary>
    /// <param name="context">Platform services (peers, discovery, config, UI, logging).</param>
    /// <returns>True if initialization succeeded; false to disable the plugin.</returns>
    Task<bool> Initialize(IPluginContext context);

    /// <summary>
    /// Called when a remote peer requests a channel for one of this plugin's capabilities.
    /// The plugin should create and return an appropriate channel, or null to reject.
    /// </summary>
    /// <param name="peer">The remote peer requesting the channel.</param>
    /// <param name="capability">The requested capability string.</param>
    /// <returns>A configured channel, or null if the request is rejected.</returns>
    Task<IChannel?> OnPeerRequest(PeerInfo peer, string capability);

    /// <summary>
    /// Shut down the plugin and release all resources.
    /// Called when the platform is stopping or the plugin is being unloaded.
    /// </summary>
    Task Shutdown();
}
