using Makaretu.Dns;
using UniLinker.Plugin.Sdk;
using Zeroconf;

namespace UniLinker.Core;

public class DiscoveryService : IDeviceDiscovery, IDisposable
{
    private static readonly string ServiceType = UniLinkerInfo.ServiceType + ".local.";
    private static readonly string ServiceName = UniLinkerInfo.ServiceType;
    private readonly string _deviceName;
    private readonly int _port;
    private readonly string _version;
    private readonly string[] _capabilities;
    private readonly List<PeerInfo> _devices = new();
    private CancellationTokenSource? _cts;

    // mDNS advertisement (Zeroconf can only browse, not announce).
    private MulticastService? _mdns;
    private ServiceDiscovery? _serviceDiscovery;

    public event Action<PeerInfo>? DeviceFound;
    public event Action<PeerInfo>? DeviceLost;
    public IReadOnlyList<PeerInfo> KnownDevices => _devices.AsReadOnly();

    public DiscoveryService(
        string deviceName = "",
        int port = 9527,
        string version = UniLinkerInfo.Version,
        string[]? capabilities = null)
    {
        _deviceName = string.IsNullOrEmpty(deviceName)
            ? Environment.MachineName : deviceName;
        _port = port;
        _version = version;
        _capabilities = capabilities ?? ["screen-capture", "file-send"];
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        StartAdvertisement();
        _ = BrowseLoopAsync(_cts.Token);
        await Task.CompletedTask;
    }

    private void StartAdvertisement()
    {
        try
        {
            _mdns = new MulticastService();
            _serviceDiscovery = new ServiceDiscovery(_mdns);

            var profile = new ServiceProfile(_deviceName, ServiceName, (ushort)_port);
            profile.AddProperty("name", _deviceName);
            profile.AddProperty("version", _version);
            profile.AddProperty("capabilities", string.Join(",", _capabilities));

            _serviceDiscovery.Advertise(profile);
            _mdns.Start();

            System.Diagnostics.Debug.WriteLine(
                $"[Discovery] Advertising '{_deviceName}' on {ServiceType} port {_port}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Discovery] advertisement failed: {ex.Message}");
            _serviceDiscovery?.Dispose();
            _serviceDiscovery = null;
            _mdns?.Dispose();
            _mdns = null;
        }
    }

    private async Task BrowseLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var results = await ZeroconfResolver.ResolveAsync(
                    ServiceType,
                    scanTime: TimeSpan.FromSeconds(3),
                    cancellationToken: ct);

                foreach (var host in results)
                {
                    var peerInfo = MapHostToPeer(host);

                    // Ignore our own advertisement.
                    if (string.Equals(peerInfo.Name, _deviceName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var existing = _devices.FirstOrDefault(d => d.Id == peerInfo.Id);

                    if (existing == null)
                    {
                        _devices.Add(peerInfo);
                        DeviceFound?.Invoke(peerInfo);
                    }
                    else
                    {
                        existing.LastSeen = DateTime.UtcNow;
                        existing.IpAddress = peerInfo.IpAddress;
                    }
                }

                // Remove stale devices (not seen in 30 seconds)
                var stale = _devices
                    .Where(d => (DateTime.UtcNow - d.LastSeen).TotalSeconds > 30)
                    .ToList();
                foreach (var d in stale)
                {
                    _devices.Remove(d);
                    DeviceLost?.Invoke(d);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Discovery browse error: {ex.Message}");
            }

            try
            {
                await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private PeerInfo MapHostToPeer(IZeroconfHost host)
    {
        var name = host.DisplayName ?? host.Id;
        var ip = host.IPAddresses?.FirstOrDefault() ?? "0.0.0.0";

        var capabilities = Array.Empty<string>();
        var version = _version;
        var port = _port;

        // Attempt to extract TXT record data
        if (host.Services?.TryGetValue(ServiceType, out var svc) == true)
        {
            port = svc.Port > 0 ? svc.Port : _port;
            if (svc.Properties != null)
            {
                foreach (var prop in svc.Properties)
                {
                    if (prop.TryGetValue("name", out var n)) name = n;
                    if (prop.TryGetValue("version", out var v)) version = v;
                    if (prop.TryGetValue("capabilities", out var c))
                        capabilities = c.Split(',');
                }
            }
        }

        return new PeerInfo
        {
            Id = host.Id,
            Name = name,
            IpAddress = ip,
            Port = port,
            Version = version,
            Capabilities = capabilities,
            State = PeerState.Discovered,
            LastSeen = DateTime.UtcNow,
        };
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        StopAdvertisement();
        return Task.CompletedTask;
    }

    private void StopAdvertisement()
    {
        try { _serviceDiscovery?.Dispose(); } catch { }
        try { _mdns?.Dispose(); } catch { }
        _serviceDiscovery = null;
        _mdns = null;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        StopAdvertisement();
    }
}
