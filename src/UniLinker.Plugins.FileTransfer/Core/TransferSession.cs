using UniLinker.Plugins.FileTransfer.Protocol;

namespace UniLinker.Plugins.FileTransfer.Core;

/// <summary>
/// Represents an active file transfer session.
/// Tracks progress, state, and provides events for monitoring.
/// </summary>
public class TransferSession : IDisposable
{
    private readonly object _lock = new();
    private bool _disposed;

    public string TransferId { get; }
    public string FilePath { get; }
    public string FileName { get; }
    public long TotalBytes { get; }
    public int TotalChunks { get; }
    public int ChunkSize { get; }
    public string FileHash { get; }
    public bool IsSender { get; }
    public TransferDirection Direction => IsSender ? TransferDirection.Sending : TransferDirection.Receiving;

    private long _transferredBytes;
    public long TransferredBytes
    {
        get { lock (_lock) return _transferredBytes; }
        private set { lock (_lock) _transferredBytes = value; }
    }

    private int _transferredChunks;
    public int TransferredChunks
    {
        get { lock (_lock) return _transferredChunks; }
        private set { lock (_lock) _transferredChunks = value; }
    }

    private TransferState _state = TransferState.Negotiating;
    public TransferState State
    {
        get { lock (_lock) return _state; }
        private set
        {
            lock (_lock)
            {
                if (_state != value)
                {
                    _state = value;
                    StateChanged?.Invoke(this);
                }
            }
        }
    }

    public float Progress => TotalBytes > 0 ? (float)TransferredBytes / TotalBytes : 0;

    private DateTime _startTime;
    public DateTime StartTime => _startTime;

    public TimeSpan Elapsed => State == TransferState.Completed
        ? _completedTime - _startTime
        : DateTime.UtcNow - _startTime;

    private DateTime _completedTime;

    public double BytesPerSecond
    {
        get
        {
            var elapsed = Elapsed.TotalSeconds;
            return elapsed > 0 ? TransferredBytes / elapsed : 0;
        }
    }

    public string? ErrorMessage { get; private set; }

    public event Action<TransferSession>? ProgressChanged;
    public event Action<TransferSession>? StateChanged;
    public event Action<TransferSession>? Completed;

    public TransferSession(
        string transferId,
        string filePath,
        string fileName,
        long totalBytes,
        int totalChunks,
        int chunkSize,
        string fileHash,
        bool isSender)
    {
        TransferId = transferId;
        FilePath = filePath;
        FileName = fileName;
        TotalBytes = totalBytes;
        TotalChunks = totalChunks;
        ChunkSize = chunkSize;
        FileHash = fileHash;
        IsSender = isSender;
    }

    public void Start()
    {
        _startTime = DateTime.UtcNow;
        State = TransferState.Transferring;
    }

    public void UpdateProgress(int chunkIndex, long bytesTransferred)
    {
        TransferredChunks = chunkIndex + 1;
        TransferredBytes = bytesTransferred;
        ProgressChanged?.Invoke(this);
    }

    public void Complete()
    {
        _completedTime = DateTime.UtcNow;
        State = TransferState.Completed;
        Completed?.Invoke(this);
    }

    public void Cancel(string? reason = null)
    {
        ErrorMessage = reason;
        State = TransferState.Cancelled;
    }

    public void Fail(string error)
    {
        ErrorMessage = error;
        State = TransferState.Error;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

public enum TransferDirection
{
    Sending,
    Receiving,
}
