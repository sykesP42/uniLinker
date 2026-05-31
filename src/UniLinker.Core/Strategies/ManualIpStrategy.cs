using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core.Strategies;

public class ManualIpStrategy : IConnectionStrategy
{
    public string Id => "manual-ip";
    public string Name => "手动输入地址";
    public string Icon => "🔢";
    public string Description => "输入对方 IP 地址和端口直接连接";
    public bool AutoDiscover => false;
    public bool NeedsRelay => false;
    public event Action<ConnectionRequest>? IncomingConnection;

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;

    public async IAsyncEnumerable<IReadOnlyList<PeerInfo>> DiscoverAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return Array.Empty<PeerInfo>().AsReadOnly();
        await Task.CompletedTask;
    }

    public static PeerInfo? ParseAddress(string address)
    {
        var trimmed = address.Trim();
        var colonIdx = trimmed.LastIndexOf(':');
        string ip;
        int port = 9527;

        if (colonIdx > 0)
        {
            ip = trimmed[..colonIdx];
            int.TryParse(trimmed[(colonIdx + 1)..], out port);
        }
        else
        {
            ip = trimmed;
        }

        if (IPAddress.TryParse(ip, out _))
        {
            return new PeerInfo
            {
                Id = $"{ip}:{port}",
                Name = ip,
                IpAddress = ip,
                Port = port,
            };
        }
        return null;
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
        $"告诉对方你的地址: {GetLocalIP()}:9527");

    private static string GetLocalIP()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
            if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                return ip.ToString();
        return "unknown";
    }
}
