using System.Runtime.InteropServices;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core.Strategies;

/// <summary>
/// Wi-Fi Direct (P2P) 连接策略
/// Windows 使用 WiFi Direct API 进行设备发现和连接
/// </summary>
public class WifiP2pStrategy : IConnectionStrategy
{
    public string Id => "wifi-p2p";
    public string Name => "Wi-Fi 直连";
    public string Icon => "📡";
    public string Description => "无需路由器，设备间直接连接";
    public bool AutoDiscover => true;
    public bool NeedsRelay => false;

    public event Action<ConnectionRequest>? IncomingConnection;

    private readonly List<PeerInfo> _discoveredPeers = new();
    private bool _isRunning;
    private CancellationTokenSource? _discoverCts;

    // Wi-Fi Direct device watcher
    private IntPtr _watcherHandle = IntPtr.Zero;

    public Task StartAsync(CancellationToken ct = default)
    {
        _isRunning = true;
        _discoverCts = new CancellationTokenSource();

        // Start discovering Wi-Fi Direct devices
        // Note: Windows WiFi Direct requires proper hardware support
        _ = DiscoverLoop(_discoverCts.Token);

        return Task.CompletedTask;
    }

    private async Task DiscoverLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isRunning)
        {
            try
            {
                // In a real implementation, this would use Windows.Devices.WiFiDirect
                // For now, we'll use a simulated discovery
                await Task.Delay(3000, ct);

                // Note: Windows WiFi Direct implementation would go here
                // This requires Windows.Devices.WiFiDirect namespace
                // which is only available in UWP or WinRT apps

                // Alternative: Use WinPcap or similar for network discovery
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public Task StopAsync()
    {
        _isRunning = false;
        _discoverCts?.Cancel();
        _discoveredPeers.Clear();
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<IReadOnlyList<PeerInfo>> DiscoverAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested && _isRunning)
        {
            await Task.Delay(2000, ct);
            yield return _discoveredPeers.ToList();
        }
    }

    public Task<IPeerMesh?> ConnectAsync(PeerInfo peer, CancellationToken ct = default)
    {
        // Wi-Fi Direct connection would establish a direct WiFi link
        // Then WebRTC would run over that link

        // For now, return null to indicate this needs implementation
        return Task.FromResult<IPeerMesh?>(null);
    }

    public ShareInfo GetShareInfo()
    {
        return new ShareInfo(
            ShareType.IpPort,
            "Wi-Fi Direct 模式",
            "点击搜索附近支持 Wi-Fi Direct 的设备"
        );
    }
}