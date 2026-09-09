namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Defines how a device can be discovered and connected to.
/// Implementations handle a specific connection method (mDNS, manual IP, connection code, etc.).
/// </summary>
public enum ShareType { IpPort, ConnectionCode, QrCodeUrl }

/// <summary>Information needed by a remote peer to connect using this strategy.</summary>
public record ShareInfo(ShareType Type, string Value, string DisplayText);

/// <summary>Represents an incoming connection request from a remote peer.</summary>
public record ConnectionRequest(
    string Id,
    PeerInfo FromDevice,
    string StrategyId,
    DateTime Timestamp);

/// <summary>
/// Connection strategy interface. Each strategy handles one way of connecting
/// (e.g., LAN mDNS discovery, manual IP entry, 6-digit connection code).
/// </summary>
public interface IConnectionStrategy
{
    /// <summary>Unique identifier for this strategy (e.g., "lan-mdns", "manual-ip").</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in the UI.</summary>
    string Name { get; }

    /// <summary>Icon identifier for the UI.</summary>
    string Icon { get; }

    /// <summary>Brief description of how this strategy works.</summary>
    string Description { get; }

    /// <summary>Whether this strategy performs automatic background discovery.</summary>
    bool AutoDiscover { get; }

    /// <summary>Whether this strategy requires a relay server (e.g., for NAT traversal).</summary>
    bool NeedsRelay { get; }

    /// <summary>Start the strategy's discovery or listening mechanism.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stop the strategy and release resources.</summary>
    Task StopAsync();

    /// <summary>
    /// Stream discovered peers as they are found.
    /// Each yield returns a snapshot of currently known peers.
    /// </summary>
    IAsyncEnumerable<IReadOnlyList<PeerInfo>> DiscoverAsync(CancellationToken ct = default);

    /// <summary>
    /// Initiate a connection to a discovered peer.
    /// </summary>
    /// <param name="peer">The peer to connect to.</param>
    /// <returns>The established peer mesh, or null if connection failed.</returns>
    Task<IPeerMesh?> ConnectAsync(PeerInfo peer, CancellationToken ct = default);

    /// <summary>Raised when a remote peer wants to connect using this strategy.</summary>
    event Action<ConnectionRequest>? IncomingConnection;

    /// <summary>Get share information (IP, code, URL) for this device using this strategy.</summary>
    ShareInfo GetShareInfo();
}
