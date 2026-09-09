namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Represents a remote (or local) UniLinker device.
/// Updated dynamically as the device is discovered, connected, or lost.
/// </summary>
public class PeerInfo
{
    /// <summary>Unique device identifier (e.g., machine name or generated UUID).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable device name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>IP address of the device (may change on network reconnect).</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>HTTP port for signaling (default: 9527).</summary>
    public int Port { get; init; }

    /// <summary>UniLinker version running on the device.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Capabilities advertised by the device (e.g., "screen-capture", "file-transfer").</summary>
    public string[] Capabilities { get; init; } = [];

    /// <summary>Current connection state of this peer.</summary>
    public PeerState State { get; set; } = PeerState.Discovered;

    /// <summary>Timestamp of the last successful communication (UTC).</summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

/// <summary>Lifecycle state of a peer connection.</summary>
public enum PeerState
{
    /// <summary>Device was found via discovery but not yet connected.</summary>
    Discovered,

    /// <summary>WebRTC connection is established and active.</summary>
    Connected,

    /// <summary>Connection was lost or explicitly disconnected.</summary>
    Disconnected
}
