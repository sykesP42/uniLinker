using UniLinker.Plugin.Sdk;
using UniLinker.Plugins.FileTransfer.Core;

namespace UniLinker.Plugins.FileTransfer;

/// <summary>
/// File Transfer Plugin for UniLinker.
/// Enables peer-to-peer file transfer between devices.
/// </summary>
public class FileTransferPlugin : IPlugin
{
    private IPluginContext? _ctx;
    private TransferManager? _transferManager;

    public string Id => "com.unilinker.file-transfer";
    public string Name => "File Transfer";
    public string Version => "1.0.0";
    public PluginPermission RequiredPermissions => PluginPermission.FileSystem;

    public string[] Capabilities => new[]
    {
        "file-send",
        "file-receive",
        "batch-transfer"
    };

    public event Action<TransferSession>? TransferStarted;
    public event Action<TransferSession>? TransferProgress;
    public event Action<TransferSession>? TransferCompleted;
    public event Action<TransferSession, string>? TransferFailed;

    public Task<bool> Initialize(IPluginContext context)
    {
        _ctx = context;
        _transferManager = new TransferManager(context);

        // Wire up transfer events
        _transferManager.TransferStarted += session => TransferStarted?.Invoke(session);
        _transferManager.TransferProgress += session => TransferProgress?.Invoke(session);
        _transferManager.TransferCompleted += session => TransferCompleted?.Invoke(session);
        _transferManager.TransferFailed += (session, error) => TransferFailed?.Invoke(session, error);

        // Register UI panel (would be done via context.UI in production)
        // For MVP, we just log initialization
        System.Diagnostics.Debug.WriteLine($"[FileTransferPlugin] Initialized");

        return Task.FromResult(true);
    }

    /// <summary>
    /// Send a file to a remote peer.
    /// </summary>
    public async Task<TransferSession?> SendFileAsync(PeerInfo peer, string filePath, string? customName = null)
    {
        if (_transferManager == null)
        {
            System.Diagnostics.Debug.WriteLine("[FileTransferPlugin] Not initialized");
            return null;
        }

        return await _transferManager.SendFileAsync(peer, filePath, customName);
    }

    /// <summary>
    /// Get all active transfers.
    /// </summary>
    public IReadOnlyList<TransferSession> GetActiveTransfers()
    {
        return _transferManager?.ActiveTransfers ?? Array.Empty<TransferSession>();
    }

    /// <summary>
    /// Cancel an active transfer.
    /// </summary>
    public void CancelTransfer(string transferId)
    {
        _transferManager?.CancelTransfer(transferId);
    }

    /// <summary>
    /// Get the default save directory for received files.
    /// </summary>
    public string GetSaveDirectory()
    {
        return _transferManager?.GetSaveDirectory() ?? "";
    }

    public Task<IChannel?> OnPeerRequest(PeerInfo peer, string capability)
    {
        // Handle incoming channel requests for file transfer
        // For MVP, we return null and let TransferManager handle channels
        return Task.FromResult<IChannel?>(null);
    }

    public Task Shutdown()
    {
        _transferManager?.Dispose();
        _transferManager = null;
        _ctx = null;
        return Task.CompletedTask;
    }
}
