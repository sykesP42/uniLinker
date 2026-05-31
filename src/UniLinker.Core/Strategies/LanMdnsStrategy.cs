using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using UniLinker.Plugin.Sdk;
using Zeroconf;

namespace UniLinker.Core.Strategies;

public class LanMdnsStrategy : IConnectionStrategy
{
    private const string ServiceType = "_unilinker._tcp.local.";
    private CancellationTokenSource? _cts;

    public string Id => "lan-mdns";
    public string Name => "局域网自动发现";
    public string Icon => "🏠";
    public string Description => "同一WiFi下自动发现设备，无需任何配置";
    public bool AutoDiscover => true;
    public bool NeedsRelay => false;
    public event Action<ConnectionRequest>? IncomingConnection;

    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<IReadOnlyList<PeerInfo>> DiscoverAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var devices = new List<PeerInfo>();
            try
            {
                var results = await ZeroconfResolver.ResolveAsync(
                    ServiceType, scanTime: TimeSpan.FromSeconds(3), cancellationToken: ct);

                foreach (var host in results)
                {
                    var ip = host.IPAddresses?.FirstOrDefault() ?? "0.0.0.0";
                    devices.Add(new PeerInfo
                    {
                        Id = host.Id,
                        Name = host.DisplayName ?? host.Id,
                        IpAddress = ip,
                        Port = host.Services?
                            .FirstOrDefault().Value?.Port ?? 9527,
                    });
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* network error, retry */ }

            yield return devices.AsReadOnly();
            await Task.Delay(5000, ct);
        }
    }

    public async Task<IPeerMesh?> ConnectAsync(PeerInfo peer, CancellationToken ct = default)
    {
        var mesh = new PeerMesh();
        var channel = await mesh.CreateChannel(peer, "screen-capture");
        return channel != null ? mesh : null;
    }

    public ShareInfo GetShareInfo() => new(
        ShareType.IpPort,
        $"{GetLocalIP()}:9527",
        "让其他设备打开 App 即可自动发现");

    private static string GetLocalIP()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
            if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                return ip.ToString();
        return "unknown";
    }
}
