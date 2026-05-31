namespace UniLinker.Plugin.Sdk;

public enum ShareType { IpPort, ConnectionCode, QrCodeUrl }

public record ShareInfo(ShareType Type, string Value, string DisplayText);

public record ConnectionRequest(
    string Id,
    PeerInfo FromDevice,
    string StrategyId,
    DateTime Timestamp);

public interface IConnectionStrategy
{
    string Id { get; }
    string Name { get; }
    string Icon { get; }
    string Description { get; }
    bool AutoDiscover { get; }
    bool NeedsRelay { get; }

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();

    IAsyncEnumerable<IReadOnlyList<PeerInfo>> DiscoverAsync(CancellationToken ct = default);

    Task<IPeerMesh?> ConnectAsync(PeerInfo peer, CancellationToken ct = default);

    event Action<ConnectionRequest>? IncomingConnection;

    ShareInfo GetShareInfo();
}
