using UniLinker.Plugin.Sdk;
using UniLinker.Plugins.FileTransfer.Protocol;

namespace UniLinker.Plugins.FileTransfer.Core;

/// <summary>
/// Handles sending files over a DataChannel.
/// Splits file into chunks and sends with flow control.
/// </summary>
public class FileSender : IDisposable
{
    private readonly IChannel _channel;
    private readonly TransferSession _session;
    private readonly CancellationTokenSource _cts = new();
    private readonly uint _transferIdCrc;
    private bool _disposed;

    public TransferSession Session => _session;

    public event Action<TransferSession>? TransferCompleted;
    public event Action<TransferSession, string>? TransferFailed;

    public FileSender(IChannel channel, string filePath, string fileName, string? senderName = null)
    {
        _channel = channel;
        _channel.MessageReceived += OnMessageReceived;

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        var transferId = Guid.NewGuid().ToString("N");
        var fileSize = fileInfo.Length;
        var chunkSize = ProtocolConstants.DefaultChunkSize;
        var totalChunks = (int)Math.Ceiling((double)fileSize / chunkSize);
        var fileHash = FileHasher.ComputeSha256Async(filePath).GetAwaiter().GetResult();

        _session = new TransferSession(
            transferId, filePath, fileName, fileSize, totalChunks, chunkSize, fileHash, isSender: true)
        {
            // Store sender name in metadata if needed
        };

        _transferIdCrc = ProtocolHandler.ComputeCrc32(transferId);
    }

    /// <summary>
    /// Start the file transfer by sending metadata.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            // Send file metadata
            var meta = new FileMetaMessage
            {
                TransferId = _session.TransferId,
                FileName = _session.FileName,
                FileSize = _session.TotalBytes,
                TotalChunks = _session.TotalChunks,
                ChunkSize = _session.ChunkSize,
                FileHash = _session.FileHash,
            };

            var metaBytes = ProtocolHandler.SerializeControlMessage(meta);
            await _channel.SendAsync(metaBytes);

            _session.Start();
        }
        catch (Exception ex)
        {
            _session.Fail(ex.Message);
            TransferFailed?.Invoke(_session, ex.Message);
        }
    }

    /// <summary>
    /// Called when receiver accepts the file.
    /// Begins sending chunks.
    /// </summary>
    public async Task SendFileAsync()
    {
        try
        {
            using var fileStream = File.OpenRead(_session.FilePath);
            var buffer = new byte[_session.ChunkSize];

            for (int chunkIndex = 0; chunkIndex < _session.TotalChunks && !_cts.IsCancellationRequested; chunkIndex++)
            {
                // Read chunk data
                var bytesRead = await fileStream.ReadAsync(buffer, 0, _session.ChunkSize, _cts.Token);
                var chunkData = bytesRead < _session.ChunkSize
                    ? buffer[..bytesRead]
                    : buffer;

                // Create and send chunk message
                var chunkMessage = ProtocolHandler.CreateChunkMessage(chunkIndex, _transferIdCrc, chunkData);
                await _channel.SendAsync(chunkMessage);

                // Update progress
                var totalBytes = (long)(chunkIndex + 1) * _session.ChunkSize;
                if (chunkIndex == _session.TotalChunks - 1)
                {
                    totalBytes = _session.TotalBytes;
                }
                _session.UpdateProgress(chunkIndex, totalBytes);

                // Flow control: wait for ACK after each window
                if ((chunkIndex + 1) % ProtocolConstants.WindowSize == 0)
                {
                    // Wait for ACK (simplified - in production would use proper async wait)
                    await Task.Delay(10, _cts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _session.Cancel("Transfer cancelled");
        }
        catch (Exception ex)
        {
            _session.Fail(ex.Message);
            TransferFailed?.Invoke(_session, ex.Message);
        }
    }

    private void OnMessageReceived(byte[] data)
    {
        var msgType = ProtocolHandler.ParseMessageType(data);
        if (msgType == null) return;

        switch (msgType.Value)
        {
            case MessageType.FileAccept:
                // Receiver accepted, start sending
                _ = SendFileAsync();
                break;

            case MessageType.FileReject:
                var reject = ProtocolHandler.DeserializeControlMessage<FileRejectMessage>(data);
                _session.Cancel(reject?.Reason ?? "Rejected by receiver");
                TransferFailed?.Invoke(_session, reject?.Reason ?? "Rejected by receiver");
                break;

            case MessageType.ChunkAck:
                // Update flow control state
                var ack = ProtocolHandler.DeserializeControlMessage<ChunkAckMessage>(data);
                if (ack != null)
                {
                    _session.UpdateProgress(ack.LastChunkIndex, ack.BytesReceived);
                }
                break;

            case MessageType.TransferComplete:
                _session.Complete();
                TransferCompleted?.Invoke(_session);
                break;

            case MessageType.TransferCancel:
                _session.Cancel("Cancelled by receiver");
                TransferFailed?.Invoke(_session, "Cancelled by receiver");
                break;

            case MessageType.TransferError:
                var error = ProtocolHandler.DeserializeControlMessage<TransferErrorMessage>(data);
                _session.Fail(error?.Message ?? "Unknown error");
                TransferFailed?.Invoke(_session, error?.Message ?? "Unknown error");
                break;
        }
    }

    public void Cancel()
    {
        _cts.Cancel();
        _session.Cancel("Cancelled by sender");

        var cancelMsg = new TransferCancelMessage { TransferId = _session.TransferId };
        var cancelBytes = ProtocolHandler.SerializeControlMessage(cancelMsg);
        _channel.SendAsync(cancelBytes).Wait();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _channel.MessageReceived -= OnMessageReceived;
    }
}
