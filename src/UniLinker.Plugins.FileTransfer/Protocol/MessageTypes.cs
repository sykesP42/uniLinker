namespace UniLinker.Plugins.FileTransfer.Protocol;

/// <summary>
/// Message types for file transfer protocol.
/// Control messages are JSON-serialized, binary chunks use a binary format.
/// </summary>
public enum MessageType : byte
{
    // Control messages (JSON)
    FileMeta = 0x01,        // File metadata (name, size, hash)
    FileAccept = 0x02,      // Accept incoming file
    FileReject = 0x03,      // Reject incoming file
    ChunkAck = 0x04,        // Acknowledge chunks received
    TransferComplete = 0x05,// Transfer completed successfully
    TransferCancel = 0x06,  // Cancel transfer
    TransferError = 0x07,   // Error during transfer

    // Binary messages
    Chunk = 0x80,           // Binary chunk data
}

/// <summary>
/// Transfer state machine states.
/// </summary>
public enum TransferState
{
    Idle,           // No transfer in progress
    Negotiating,    // Waiting for accept/reject
    Transferring,   // Data being transferred
    Completed,      // Transfer finished successfully
    Cancelled,      // Transfer cancelled by user
    Error,          // Transfer failed with error
}

/// <summary>
/// Default protocol constants.
/// </summary>
public static class ProtocolConstants
{
    /// <summary>Default chunk size: 16KB</summary>
    public const int DefaultChunkSize = 16384;

    /// <summary>Maximum chunk size: 64KB</summary>
    public const int MaxChunkSize = 65536;

    /// <summary>Window size in chunks (for flow control)</summary>
    public const int WindowSize = 16;

    /// <summary>Chunk header size: 1 (type) + 4 (index) + 4 (transferId CRC)</summary>
    public const int ChunkHeaderSize = 9;
}
