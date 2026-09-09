namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Provides plugins with access to platform services.
/// Passed to <see cref="IPlugin.Initialize"/> during plugin startup.
/// </summary>
public interface IPluginContext
{
    /// <summary>Manage WebRTC connections to other peers.</summary>
    IPeerMesh Peers { get; }

    /// <summary>Discover other UniLinker devices on the network.</summary>
    IDeviceDiscovery Discovery { get; }

    /// <summary>Persist and retrieve plugin configuration.</summary>
    IConfigStore Config { get; }

    /// <summary>Register UI panels and tray actions (platform-specific).</summary>
    IUIProvider UI { get; }

    /// <summary>Metadata about this plugin instance.</summary>
    PluginInfo Self { get; }

    /// <summary>Structured logging for debug, info, warn, and error levels.</summary>
    IPluginLogger Logger { get; }
}
