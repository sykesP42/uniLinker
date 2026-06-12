using UniLinker.Plugin.Sdk;
using UniLinker.Plugins.FileTransfer.Protocol;

namespace UniLinker.Plugins.FileTransfer.Core;

/// <summary>
/// Handles receiving files over a DataChannel.
/// Reassembles chunks and verifies integrity.
/// </summary>
public class FileReceiver : IDisposable
{
    private readonly IChannel _channel;
    private readonly string _saveDirectory;
    private TransferSession? _session;
    private FileStream? _fileStream;
    private readonly Dictionary<int, byte[]> _chunkBuffer = new();
    private int _expectedChunkIndex;
    private long _bytesReceived;
    private bool _disposed;
    private string? _pendingFilePath;

    public event Action<FileMetaMessage, FileReceiver>? FileOffered;
    public event Action<TransferSession>? TransferCompleted;
    public event Action<TransferSession, string>? TransferFailed;

    public FileReceiver(IChannel channel, string saveDirectory)
    {
        _channel = channel;
        _saveDirectory = saveDirectory;
        _channel.MessageReceived += OnMessageReceived;
    }

    public TransferSession? Session => _session;

    private void OnMessageReceived(byte[] data)
    {
        var msgType = ProtocolHandler.ParseMessageType(data);
        if (msgType == null) return;

        switch (msgType.Value)
        {
            case MessageType.FileMeta:
                HandleFileMeta(data);
                break;

            case MessageType.Chunk:
                HandleChunk(data);
                break;

            case MessageType.TransferCancel:
                _session?.Cancel("Cancelled by sender");
                TransferFailed?.Invoke(_session!, "Cancelled by sender");
                Cleanup();
                break;
        }
    }

    private void HandleFileMeta(byte[] data)
    {
        var meta = ProtocolHandler.DeserializeControlMessage<FileMetaMessage>(data);
        if (meta == null) return;

        // Create session
        _session = new TransferSession(
            meta.TransferId,
            "", // Will be set when accepted
            meta.FileName,
            meta.FileSize,
            meta.TotalChunks,
            meta.ChunkSize,
            meta.FileHash,
            isSender: false);

        _expectedChunkIndex = 0;
        _bytesReceived = 0;

        // Notify that a file is being offered
        FileOffered?.Invoke(meta, this);
    }

    /// <summary>
    /// Accept the incoming file transfer.
    /// </summary>
    public async Task AcceptAsync(string? customSavePath = null)
    {
        if (_session == null) return;

        try
        {
            Directory.CreateDirectory(_saveDirectory);

            // Sanitize filename and create save path
            var fileName = SanitizeFileName(_session.FileName);
            var savePath = customSavePath ?? Path.Combine(_saveDirectory, fileName);

            // Handle duplicate filenames
            savePath = GetUniqueFilePath(savePath);

            _session = new TransferSession(
                _session.TransferId,
                savePath,
                _session.FileName,
                _session.TotalBytes,
                _session.TotalChunks,
                _session.ChunkSize,
                _session.FileHash,
                isSender: false);

            _pendingFilePath = savePath;
            _fileStream = File.Create(savePath);

            _session.Start();

            // Send accept message
            var acceptMsg = new FileAcceptMessage { TransferId = _session.TransferId };
            var acceptBytes = ProtocolHandler.SerializeControlMessage(acceptMsg);
            await _channel.SendAsync(acceptBytes);
        }
        catch (Exception ex)
        {
            _session.Fail(ex.Message);
            TransferFailed?.Invoke(_session, ex.Message);
            await RejectAsync(ex.Message);
        }
    }

    /// <summary>
    /// Reject the incoming file transfer.
    /// </summary>
    public async Task RejectAsync(string? reason = null)
    {
        if (_session == null) return;

        var rejectMsg = new FileRejectMessage
        {
            TransferId = _session.TransferId,
            Reason = reason
        };
        var rejectBytes = ProtocolHandler.SerializeControlMessage(rejectMsg);
        await _channel.SendAsync(rejectBytes);

        Cleanup();
    }

    private void HandleChunk(byte[] data)
    {
        if (_session == null || _fileStream == null) return;

        try
        {
            var chunk = ProtocolHandler.ParseChunkMessage(data);
            if (chunk == null) return;

            // Verify this is the expected chunk
            if (chunk.ChunkIndex != _expectedChunkIndex)
            {
                // Buffer out-of-order chunks (shouldn't happen with ordered channel)
                _chunkBuffer[chunk.ChunkIndex] = chunk.Data;
                return;
            }

            // Write chunk to file
            _fileStream.Write(chunk.Data, 0, chunk.Data.Length);
            _bytesReceived += chunk.Data.Length;
            _expectedChunkIndex++;

            // Process buffered chunks
            while (_chunkBuffer.TryGetValue(_expectedChunkIndex, out var bufferedChunk))
            {
                _fileStream.Write(bufferedChunk, 0, bufferedChunk.Length);
                _bytesReceived += bufferedChunk.Length;
                _expectedChunkIndex++;
                _chunkBuffer.Remove(_expectedChunkIndex - 1);
            }

            // Update progress
            _session.UpdateProgress(_expectedChunkIndex - 1, _bytesReceived);

            // Send ACK after each window
            if (_expectedChunkIndex % ProtocolConstants.WindowSize == 0 ||
                _expectedChunkIndex == _session.TotalChunks)
            {
                SendAckAsync().Wait();
            }

            // Check if transfer is complete
            if (_expectedChunkIndex >= _session.TotalChunks)
            {
                _ = FinalizeTransferAsync();
            }
        }
        catch (Exception ex)
        {
            _session.Fail(ex.Message);
            TransferFailed?.Invoke(_session, ex.Message);
            SendErrorAsync(ex.Message).Wait();
        }
    }

    private async Task SendAckAsync()
    {
        if (_session == null) return;

        var ackMsg = new ChunkAckMessage
        {
            TransferId = _session.TransferId,
            LastChunkIndex = _expectedChunkIndex - 1,
            BytesReceived = _bytesReceived
        };
        var ackBytes = ProtocolHandler.SerializeControlMessage(ackMsg);
        await _channel.SendAsync(ackBytes);
    }

    private async Task FinalizeTransferAsync()
    {
        if (_session == null || _fileStream == null) return;

        try
        {
            // Close file stream
            await _fileStream.FlushAsync();
            _fileStream.Dispose();
            _fileStream = null;

            // Verify hash
            var hashVerified = await FileHasher.VerifyFileAsync(_session.FilePath, _session.FileHash);

            if (hashVerified)
            {
                _session.Complete();
                TransferCompleted?.Invoke(_session);

                // Send completion message
                var completeMsg = new TransferCompleteMessage
                {
                    TransferId = _session.TransferId,
                    HashVerified = true
                };
                var completeBytes = ProtocolHandler.SerializeControlMessage(completeMsg);
                await _channel.SendAsync(completeBytes);
            }
            else
            {
                _session.Fail("Hash verification failed");
                TransferFailed?.Invoke(_session, "Hash verification failed");

                await SendErrorAsync("Hash verification failed");
            }
        }
        catch (Exception ex)
        {
            _session.Fail(ex.Message);
            TransferFailed?.Invoke(_session, ex.Message);
            await SendErrorAsync(ex.Message);
        }
    }

    private async Task SendErrorAsync(string message)
    {
        if (_session == null) return;

        var errorMsg = new TransferErrorMessage
        {
            TransferId = _session.TransferId,
            Message = message
        };
        var errorBytes = ProtocolHandler.SerializeControlMessage(errorMsg);
        await _channel.SendAsync(errorBytes);
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var result = new System.Text.StringBuilder(fileName);
        foreach (var c in invalidChars)
        {
            result.Replace(c, '_');
        }
        return result.ToString();
    }

    private static string GetUniqueFilePath(string basePath)
    {
        if (!File.Exists(basePath)) return basePath;

        var dir = Path.GetDirectoryName(basePath) ?? "";
        var name = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);

        var counter = 1;
        string newPath;
        do
        {
            newPath = Path.Combine(dir, $"{name} ({counter}){ext}");
            counter++;
        } while (File.Exists(newPath));

        return newPath;
    }

    private void Cleanup()
    {
        _fileStream?.Dispose();
        _fileStream = null;
        _chunkBuffer.Clear();
        _session = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.MessageReceived -= OnMessageReceived;
        _fileStream?.Dispose();
        _chunkBuffer.Clear();
    }
}
