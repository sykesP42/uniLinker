using System.Runtime.InteropServices;
using System.Text.Json;
using UniLinker.Core;
using UniLinker.Plugin.Sdk;

namespace UniLinker.WinUI.Services;

public class WebBridge
{
    public Platform Platform { get; }
    private readonly Platform _platform;

    public WebBridge(Platform platform)
    {
        _platform = platform;
        Platform = platform;
    }

    // Get discovered devices for native UI
    public IReadOnlyList<PeerInfo> GetDiscoveredDevices()
    {
        return _platform.Discovery?.KnownDevices ?? Array.Empty<PeerInfo>().AsReadOnly();
    }

    // Get status info for native UI
    public (string DeviceName, int Port, int Peers) GetStatusInfo()
    {
        var config = _platform.Context.Config.Get<PlatformConfig>("platform");
        return (
            config.DeviceName ?? Environment.MachineName,
            config.HttpPort > 0 ? config.HttpPort : 9527,
            _platform.PeerMesh?.ConnectedPeers.Count ?? 0
        );
    }

    // Called from JS: GetDevices()
    public string GetDevices()
    {
        var devices = _platform.Discovery?.KnownDevices
            ?? Array.Empty<PeerInfo>().AsReadOnly();
        return JsonSerializer.Serialize(devices);
    }

    // Called from JS: GetStatus()
    public string GetStatus()
    {
        var config = new PlatformConfig();
        try
        {
            config = _platform.Context.Config.Get<PlatformConfig>("platform");
        }
        catch { }

        return JsonSerializer.Serialize(new
        {
            status = "running",
            deviceName = config.DeviceName,
            port = config.HttpPort,
            peers = _platform.PeerMesh?.ConnectedPeers.Count ?? 0,
            version = "3.0.0",
        });
    }

    // Called from JS: StartSharing(configJson)
    public async void StartSharing(string configJson)
    {
        try
        {
            var opts = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson);
            // Update screen-mirror config so the plugin picks it up on next channel request
            _platform.Context.Config.Set("screen-mirror", new
            {
                CaptureWidth = opts?.GetValueOrDefault("width", default).GetInt32() ?? 1920,
                CaptureHeight = opts?.GetValueOrDefault("height", default).GetInt32() ?? 1080,
                CaptureFps = opts?.GetValueOrDefault("fps", default).GetInt32() ?? 30,
                BitrateKbps = opts?.GetValueOrDefault("bitrate", default).GetInt32() ?? 15000,
            });
            await _platform.Context.Config.SaveAsync();
            System.Diagnostics.Debug.WriteLine($"StartSharing: configured {configJson}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StartSharing error: {ex.Message}");
        }
    }

    // Called from JS: StopSharing()
    public async void StopSharing()
    {
        try
        {
            if (_platform.PeerMesh != null)
            {
                var peers = _platform.PeerMesh.ConnectedPeers;
                foreach (var peer in peers)
                {
                    await _platform.PeerMesh.DisconnectPeer(peer);
                }
            }
            System.Diagnostics.Debug.WriteLine("StopSharing: disconnected all peers");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StopSharing error: {ex.Message}");
        }
    }

    // Called from JS: WatchDevice(peerId)
    public async void WatchDevice(string peerId)
    {
        try
        {
            var peer = _platform.Discovery?.KnownDevices
                .FirstOrDefault(p => p.Id == peerId);
            if (peer == null)
            {
                System.Diagnostics.Debug.WriteLine($"WatchDevice: peer {peerId} not found");
                return;
            }

            // Request screen-capture from peer — triggers their ChannelRequested handler
            var channel = await (_platform.PeerMesh?.CreateChannel(peer, "screen-capture") ?? Task.FromResult<IChannel?>(null));

            if (channel != null)
            {
                // Subscribe to received packets (depacketized NAL units from RTP)
                var frameCount = 0;
                var keyFrameCount = 0;
                channel.PacketReceived += packet =>
                {
                    frameCount++;
                    if (packet.IsKeyFrame) keyFrameCount++;
                    if (frameCount % 60 == 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Watch] Received {frameCount} frames ({keyFrameCount} keyframes) " +
                            $"from {peer.Name}, last packet {packet.Data.Length} bytes");
                    }
                };

                System.Diagnostics.Debug.WriteLine(
                    $"WatchDevice: channel opened to {peer.Name}, receiving on local port");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WatchDevice error: {ex.Message}");
        }
    }

    // Called from JS: StopWatching(peerId)
    public async void StopWatching(string peerId)
    {
        try
        {
            var peers = _platform.PeerMesh?.ConnectedPeers ?? [];
            var peer = peers.FirstOrDefault(p => p.Id == peerId);
            if (peer != null)
            {
                await (_platform.PeerMesh?.DisconnectPeer(peer) ?? Task.CompletedTask);
                System.Diagnostics.Debug.WriteLine($"StopWatching: disconnected from {peer.Name}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StopWatching error: {ex.Message}");
        }
    }

    // Called from JS: GetConfig()
    public string GetConfig()
    {
        var config = _platform.Context.Config.Get<PlatformConfig>("platform");
        return JsonSerializer.Serialize(config);
    }

    // Called from JS: SaveConfig(configJson)
    public void SaveConfig(string configJson)
    {
        try
        {
            var config = JsonSerializer.Deserialize<PlatformConfig>(configJson);
            if (config != null)
            {
                _platform.Context.Config.Set("platform", config);
                _ = _platform.Context.Config.SaveAsync();
            }
        }
        catch { }
    }
}

// COM-visible wrapper for WebView2 host object
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDispatch)]
public class CoreWebView2Bridge
{
    private readonly WebBridge _bridge;
    public CoreWebView2Bridge(WebBridge bridge) => _bridge = bridge;
    public string GetDevices() => _bridge.GetDevices();
    public string GetStatus() => _bridge.GetStatus();
    public void StartSharing(string configJson) => _bridge.StartSharing(configJson);
    public void StopSharing() => _bridge.StopSharing();
    public void WatchDevice(string peerId) => _bridge.WatchDevice(peerId);
    public void StopWatching(string peerId) => _bridge.StopWatching(peerId);
    public string GetConfig() => _bridge.GetConfig();
    public void SaveConfig(string configJson) => _bridge.SaveConfig(configJson);
}
