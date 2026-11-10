using System.Runtime.InteropServices;
using System.Text.Json;
using UniLinker.Core;
using UniLinker.Plugin.Sdk;
using UniLinker.Plugins.FileTransfer;
using UniLinker.Plugins.FileTransfer.Core;

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

    /// <summary>
    /// Get the FileTransfer plugin instance.
    /// </summary>
    public FileTransferPlugin? FileTransfer =>
        Platform.PluginHost?.Plugins
            .FirstOrDefault(p => p.Plugin.Id == "com.unilinker.file-transfer")?
            .Plugin as FileTransferPlugin;

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
            version = UniLinkerInfo.Version,
        });
    }

    /// <summary>
    /// Update the screen-mirror config so the plugin picks it up on the next
    /// channel request. Values are optional; missing keys keep defaults.
    /// </summary>
    public async void StartSharing(string configJson)
    {
        try
        {
            var opts = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configJson)
                       ?? new Dictionary<string, JsonElement>();

            static int GetInt(Dictionary<string, JsonElement> o, string key, int fallback)
                => o.TryGetValue(key, out var el) && el.TryGetInt32(out var v) && v > 0 ? v : fallback;

            _platform.Context.Config.Set("screen-mirror", new
            {
                CaptureWidth = GetInt(opts, "width", 1920),
                CaptureHeight = GetInt(opts, "height", 1080),
                CaptureFps = GetInt(opts, "fps", 30),
                BitrateKbps = GetInt(opts, "bitrate", 8000),
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

    #region FileTransfer Methods

    /// <summary>
    /// Send a file to a remote peer.
    /// </summary>
    public async Task<bool> SendFileAsync(PeerInfo peer, string filePath)
    {
        if (FileTransfer == null) return false;
        var session = await FileTransfer.SendFileAsync(peer, filePath);
        return session != null;
    }

    /// <summary>
    /// Get all active file transfers.
    /// </summary>
    public IReadOnlyList<TransferSession> GetActiveTransfers()
    {
        return FileTransfer?.GetActiveTransfers() ?? Array.Empty<TransferSession>();
    }

    /// <summary>
    /// Cancel an active file transfer.
    /// </summary>
    public void CancelTransfer(string transferId)
    {
        FileTransfer?.CancelTransfer(transferId);
    }

    /// <summary>
    /// Get the default save directory for received files.
    /// </summary>
    public string GetFileTransferSaveDirectory()
    {
        return FileTransfer?.GetSaveDirectory() ?? "";
    }

    #endregion
}
