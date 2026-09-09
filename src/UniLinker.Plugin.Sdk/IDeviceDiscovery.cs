namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Device discovery service. Finds other UniLinker devices on the local network
/// and notifies when they appear or disappear.
/// </summary>
public interface IDeviceDiscovery
{
    /// <summary>Raised when a new device is found on the network.</summary>
    event Action<PeerInfo>? DeviceFound;

    /// <summary>Raised when a previously seen device goes offline.</summary>
    event Action<PeerInfo>? DeviceLost;

    /// <summary>Snapshot of all currently known devices.</summary>
    IReadOnlyList<PeerInfo> KnownDevices { get; }

    /// <summary>Start discovery (e.g., mDNS browsing, UDP broadcast listener).</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stop discovery and release network resources.</summary>
    Task StopAsync();
}
