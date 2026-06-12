using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.FileTransfer.Core;

/// <summary>
/// Manages multiple active file transfers.
/// Coordinates sending and receiving operations.
/// </summary>
public class TransferManager : IDisposable
{
    private readonly IPluginContext _context;
    private readonly Dictionary<string, FileSender> _senders = new();
    private readonly Dictionary<string, FileReceiver> _receivers = new();
    private readonly object _lock = new();
    private readonly string _defaultSaveDirectory;
    private bool _disposed;

    public event Action<TransferSession>? TransferStarted;
    public event Action<TransferSession>? TransferProgress;
    public event Action<TransferSession>? TransferCompleted;
    public event Action<TransferSession, string>? TransferFailed;

    public IReadOnlyList<TransferSession> ActiveTransfers
    {
        get
        {
            lock (_lock)
            {
                var result = new List<TransferSession>();
                result.AddRange(_senders.Values.Select(s => s.Session));
                result.AddRange(_receivers.Values.Where(r => r.Session != null).Select(r => r.Session!));
                return result.AsReadOnly();
            }
        }
    }

    public TransferManager(IPluginContext context)
    {
        _context = context;
        _defaultSaveDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "UniLinker");
    }

    /// <summary>
    /// Send a file to a remote peer.
    /// </summary>
    public async Task<TransferSession?> SendFileAsync(PeerInfo peer, string filePath, string? customName = null)
    {
        try
        {
            // Create DataChannel
            var options = new ChannelOptions
            {
                Type = ChannelType.DataChannel,
                Label = "file-transfer",
                Ordered = true
            };

            var channel = await _context.Peers.CreateChannel(peer, "file-send", options);
            if (channel == null)
            {
                return null;
            }

            var fileName = customName ?? Path.GetFileName(filePath);
            var sender = new FileSender(channel, filePath, fileName);

            // Wire up events
            sender.TransferCompleted += session => TransferCompleted?.Invoke(session);
            sender.TransferFailed += (session, error) => TransferFailed?.Invoke(session, error);

            lock (_lock)
            {
                _senders[sender.Session.TransferId] = sender;
            }

            TransferStarted?.Invoke(sender.Session);

            // Start the transfer
            await sender.StartAsync();

            return sender.Session;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TransferManager] SendFile error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Create a receiver for incoming DataChannel.
    /// </summary>
    public FileReceiver CreateReceiver(IChannel channel)
    {
        var receiver = new FileReceiver(channel, _defaultSaveDirectory);

        // Wire up events
        receiver.FileOffered += OnFileOffered;
        receiver.TransferCompleted += session => TransferCompleted?.Invoke(session);
        receiver.TransferFailed += (session, error) => TransferFailed?.Invoke(session, error);

        return receiver;
    }

    private void OnFileOffered(Protocol.FileMetaMessage meta, FileReceiver receiver)
    {
        // Auto-accept for now (in production, would show UI prompt)
        // For MVP, we auto-accept all incoming files
        _ = receiver.AcceptAsync();
    }

    /// <summary>
    /// Cancel an active transfer.
    /// </summary>
    public void CancelTransfer(string transferId)
    {
        lock (_lock)
        {
            if (_senders.TryGetValue(transferId, out var sender))
            {
                sender.Cancel();
                _senders.Remove(transferId);
            }
        }
    }

    /// <summary>
    /// Get the default save directory for received files.
    /// </summary>
    public string GetSaveDirectory() => _defaultSaveDirectory;

    /// <summary>
    /// Set a custom save directory for received files.
    /// </summary>
    public void SetSaveDirectory(string path)
    {
        // In production, would store in config
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            foreach (var sender in _senders.Values)
                sender.Dispose();
            foreach (var receiver in _receivers.Values)
                receiver.Dispose();
            _senders.Clear();
            _receivers.Clear();
        }
    }
}
