using System.Runtime.InteropServices;
using System.Text.Json;
using UniLinker.Core;
using UniLinker.Plugin.Sdk;

namespace UniLinker.UI;

public class WebBridge
{
    private readonly Platform _platform;

    public WebBridge(Platform platform)
    {
        _platform = platform;
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
    public void StartSharing(string configJson)
    {
        System.Diagnostics.Debug.WriteLine($"StartSharing: {configJson}");
    }

    // Called from JS: StopSharing()
    public void StopSharing()
    {
        System.Diagnostics.Debug.WriteLine("StopSharing called");
    }

    // Called from JS: WatchDevice(peerId)
    public void WatchDevice(string peerId)
    {
        System.Diagnostics.Debug.WriteLine($"WatchDevice: {peerId}");
    }

    // Called from JS: StopWatching(peerId)
    public void StopWatching(string peerId)
    {
        System.Diagnostics.Debug.WriteLine($"StopWatching: {peerId}");
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
