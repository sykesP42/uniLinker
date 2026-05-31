using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class DiscoveryService : IDeviceDiscovery, IDisposable
{
    private readonly List<PeerInfo> _devices = new();

#pragma warning disable CS0067
    public event Action<PeerInfo>? DeviceFound;
    public event Action<PeerInfo>? DeviceLost;
#pragma warning restore CS0067
    public IReadOnlyList<PeerInfo> KnownDevices => _devices.AsReadOnly();

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public void Dispose() { }
}
