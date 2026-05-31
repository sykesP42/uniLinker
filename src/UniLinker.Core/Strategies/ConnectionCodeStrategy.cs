using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core.Strategies;

public class ConnectionCodeStrategy : IConnectionStrategy, IDisposable
{
    private const int BroadcastPort = 9528;
    private const string CodePrefix = "UNILINKER_CODE:";
    private const int CodeLength = 6;

    private string? _myCode;
    private UdpClient? _broadcaster;
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;

    public string Id => "connection-code";
    public string Name => "连接码";
    public string Icon => "🔗";
    public string Description => "输入对方屏幕上显示的 6 位连接码";
    public bool AutoDiscover => false;
    public bool NeedsRelay => false;
    public event Action<ConnectionRequest>? IncomingConnection;

    /// <summary>Generate a 6-digit connection code and start broadcasting.</summary>
    public string GenerateCode()
    {
        _myCode = Random.Shared.Next(100000, 999999).ToString();
        _ = StartBroadcastAsync();
        return _myCode;
    }

    /// <summary>Listen for a specific code being broadcast.</summary>
    public async IAsyncEnumerable<PeerInfo> ListenForCodeAsync(
        string targetCode,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _listener = new UdpClient(BroadcastPort);
        _listener.Client.ReceiveTimeout = 5000;

        PeerInfo? found = null;

        while (!ct.IsCancellationRequested && found == null)
        {
            UdpReceiveResult result = default;
            bool gotPacket = false;

            try
            {
                result = await _listener.ReceiveAsync(ct);
                gotPacket = true;
            }
            catch (OperationCanceledException) { break; }
            catch { /* timeout, continue */ }

            if (gotPacket)
            {
                var msg = Encoding.UTF8.GetString(result.Buffer);
                if (msg.StartsWith(CodePrefix))
                {
                    var payload = msg[CodePrefix.Length..];
                    var parts = payload.Split('|');
                    if (parts.Length == 2 && parts[0] == targetCode)
                    {
                        var addrParts = parts[1].Split(':');
                        found = new PeerInfo
                        {
                            Id = parts[0],
                            Name = $"设备 ({targetCode})",
                            IpAddress = addrParts[0],
                            Port = int.TryParse(addrParts[1], out var p) ? p : 9527,
                        };
                    }
                }
            }
        }

        if (found != null)
            yield return found;
    }

    private async Task StartBroadcastAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _broadcaster = new UdpClient { EnableBroadcast = true };

        var code = _myCode;
        var localIp = GetLocalIP();
        var message = Encoding.UTF8.GetBytes($"{CodePrefix}{code}|{localIp}:9527");

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await _broadcaster.SendAsync(
                    message, message.Length,
                    new IPEndPoint(IPAddress.Broadcast, BroadcastPort));
            }
            catch { }
            await Task.Delay(1000, _cts.Token);
        }
    }

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task StopAsync()
    {
        _cts?.Cancel();
        _broadcaster?.Dispose();
        _listener?.Dispose();
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<IReadOnlyList<PeerInfo>> DiscoverAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return Array.Empty<PeerInfo>().AsReadOnly();
        await Task.CompletedTask;
    }

    public async Task<IPeerMesh?> ConnectAsync(PeerInfo peer, CancellationToken ct = default)
    {
        var mesh = new PeerMesh();
        var channel = await mesh.CreateChannel(peer, "screen-capture");
        return channel != null ? mesh : null;
    }

    public ShareInfo GetShareInfo()
    {
        _myCode ??= GenerateCode();
        return new ShareInfo(ShareType.ConnectionCode, _myCode, $"连接码: {_myCode}");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _broadcaster?.Dispose();
        _listener?.Dispose();
    }

    private static string GetLocalIP()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
            if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                return ip.ToString();
        return "unknown";
    }
}
