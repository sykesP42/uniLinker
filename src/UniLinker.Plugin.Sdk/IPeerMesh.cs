namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Manages WebRTC connections (the "mesh") between this device and remote peers.
/// Plugins use this to create channels and communicate with connected peers.
/// </summary>
public interface IPeerMesh
{
    /// <summary>Snapshot of currently connected peers.</summary>
    IReadOnlyList<PeerInfo> ConnectedPeers { get; }

    /// <summary>Raised when a new peer connection is established.</summary>
    event Action<PeerInfo>? PeerConnected;

    /// <summary>Raised when a peer disconnects.</summary>
    event Action<PeerInfo>? PeerDisconnected;

    /// <summary>
    /// Raised when a remote peer requests a channel for a specific capability.
    /// Handlers should return a channel or null to reject.
    /// </summary>
    event Func<PeerInfo, string, Task<IChannel?>>? ChannelRequested;

    /// <summary>
    /// Create a channel to a remote peer for a given capability.
    /// </summary>
    /// <param name="peer">Target peer (must be connected).</param>
    /// <param name="capability">Capability string (e.g., "screen-capture").</param>
    /// <param name="options">Optional channel configuration (type, ordering, etc.).</param>
    /// <returns>The created channel, or null if creation failed.</returns>
    Task<IChannel?> CreateChannel(PeerInfo peer, string capability, ChannelOptions? options = null);

    /// <summary>Disconnect from a specific peer and release associated resources.</summary>
    Task DisconnectPeer(PeerInfo peer);
}
