using System.Text.Json.Serialization;

namespace UniLinker.Plugins.FileTransfer.Protocol;

/// <summary>
/// File metadata message sent before transfer begins.
/// Contains all information needed for the receiver to decide whether to accept.
/// </summary>
public class FileMetaMessage
{
    /// <summary>Message type identifier</summary>
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.FileMeta;

    /// <summary>Unique identifier for this transfer session</summary>
    [JsonPropertyName("transferId")]
    public string TransferId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Original file name</summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    /// <summary>File size in bytes</summary>
    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    /// <summary>Number of chunks to be sent</summary>
    [JsonPropertyName("totalChunks")]
    public int TotalChunks { get; set; }

    /// <summary>Chunk size used for this transfer</summary>
    [JsonPropertyName("chunkSize")]
    public int ChunkSize { get; set; } = ProtocolConstants.DefaultChunkSize;

    /// <summary>SHA-256 hash of the file for integrity verification</summary>
    [JsonPropertyName("fileHash")]
    public string FileHash { get; set; } = "";

    /// <summary>MIME type if known</summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>Sender's display name</summary>
    [JsonPropertyName("senderName")]
    public string? SenderName { get; set; }
}

/// <summary>
/// Accept message sent by receiver to confirm transfer.
/// </summary>
public class FileAcceptMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.FileAccept;

    [JsonPropertyName("transferId")]
    public string TransferId { get; set; } = "";

    /// <summary>Save path chosen by receiver</summary>
    [JsonPropertyName("savePath")]
    public string? SavePath { get; set; }
}

/// <summary>
/// Reject message sent by receiver to decline transfer.
/// </summary>
public class FileRejectMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.FileReject;

    [JsonPropertyName("transferId")]
    public string TransferId { get; set; } = "";

    /// <summary>Optional reason for rejection</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// Chunk acknowledgment message sent by receiver after receiving a window of chunks.
/// </summary>
public class ChunkAckMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.ChunkAck;

    [JsonPropertyName("transferId")]
    public string TransferId { get; set; } = "";

    /// <summary>Last chunk index successfully received</summary>
    [JsonPropertyName("lastChunkIndex")]
    public int LastChunkIndex { get; set; }

    /// <summary>Total bytes received so far</summary>
    [JsonPropertyName("bytesReceived")]
    public long BytesReceived { get; set; }
}

/// <summary>
/// Transfer complete message sent by receiver after all chunks received and verified.
/// </summary>
public class TransferCompleteMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.TransferComplete;

    [JsonPropertyName("transferId")]
    public string TransferId { get; set; } = "";

    /// <summary>Whether file hash matched</summary>
    [JsonPropertyName("hashVerified")]
    public bool HashVerified { get; set; }
}

/// <summary>
/// Cancel message sent by either party to abort transfer.
/// </summary>
public class TransferCancelMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.TransferCancel;

    [JsonPropertyName("transferId")]
    public string TransferId { get; set; } = "";

    /// <summary>Optional reason for cancellation</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// Error message sent when something goes wrong during transfer.
/// </summary>
public class TransferErrorMessage
{
    [JsonPropertyName("type")]
    public MessageType Type => MessageType.TransferError;

    [JsonPropertyName("transferId")]
    public string TransferId { get; set; } = "";

    [JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}