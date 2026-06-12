using System.Text.Json;

namespace UniLinker.Plugins.FileTransfer.Protocol;

/// <summary>
/// Handles serialization and deserialization of protocol messages.
/// Control messages are JSON, binary chunks have a specific format.
/// </summary>
public static class ProtocolHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Serialize a control message to JSON bytes.
    /// </summary>
    public static byte[] SerializeControlMessage(object message)
    {
        return JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
    }

    /// <summary>
    /// Deserialize a control message from JSON bytes.
    /// </summary>
    public static T? DeserializeControlMessage<T>(byte[] data) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(data, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parse the message type from the first byte of raw data.
    /// Returns null if the message format is invalid.
    /// </summary>
    public static MessageType? ParseMessageType(byte[] data)
    {
        if (data == null || data.Length == 0)
            return null;

        // Check if it's a binary chunk (starts with 0x80)
        if (data[0] == (byte)MessageType.Chunk)
            return MessageType.Chunk;

        // Otherwise, try to parse as JSON
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(data, JsonOptions);
            if (json.TryGetProperty("type", out var typeProp))
            {
                var typeStr = typeProp.GetString();
                if (typeStr != null && Enum.TryParse<MessageType>(typeStr, out var type))
                {
                    return type;
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Create a binary chunk message.
    /// Format: [0x80][chunkIndex: 4 bytes LE][transferIdCrc: 4 bytes LE][data]
    /// </summary>
    public static byte[] CreateChunkMessage(int chunkIndex, uint transferIdCrc, byte[] chunkData)
    {
        var message = new byte[ProtocolConstants.ChunkHeaderSize + chunkData.Length];
        message[0] = (byte)MessageType.Chunk;

        // Write chunk index (4 bytes, little endian)
        message[1] = (byte)(chunkIndex & 0xFF);
        message[2] = (byte)((chunkIndex >> 8) & 0xFF);
        message[3] = (byte)((chunkIndex >> 16) & 0xFF);
        message[4] = (byte)((chunkIndex >> 24) & 0xFF);

        // Write transfer ID CRC (4 bytes, little endian)
        message[5] = (byte)(transferIdCrc & 0xFF);
        message[6] = (byte)((transferIdCrc >> 8) & 0xFF);
        message[7] = (byte)((transferIdCrc >> 16) & 0xFF);
        message[8] = (byte)((transferIdCrc >> 24) & 0xFF);

        // Copy chunk data
        Buffer.BlockCopy(chunkData, 0, message, ProtocolConstants.ChunkHeaderSize, chunkData.Length);

        return message;
    }

    /// <summary>
    /// Parse a binary chunk message.
    /// Returns null if the message format is invalid.
    /// </summary>
    public static ChunkData? ParseChunkMessage(byte[] data)
    {
        if (data == null || data.Length < ProtocolConstants.ChunkHeaderSize)
            return null;

        if (data[0] != (byte)MessageType.Chunk)
            return null;

        var chunkIndex = data[1] | (data[2] << 8) | (data[3] << 16) | (data[4] << 24);
        var transferIdCrc = (uint)(data[5] | (data[6] << 8) | (data[7] << 16) | (data[8] << 24));

        var chunkData = new byte[data.Length - ProtocolConstants.ChunkHeaderSize];
        Buffer.BlockCopy(data, ProtocolConstants.ChunkHeaderSize, chunkData, 0, chunkData.Length);

        return new ChunkData
        {
            ChunkIndex = chunkIndex,
            TransferIdCrc = transferIdCrc,
            Data = chunkData
        };
    }

    /// <summary>
    /// Compute CRC32 of a string for quick identification.
    /// </summary>
    public static uint ComputeCrc32(string text)
    {
        var data = System.Text.Encoding.UTF8.GetBytes(text);
        return ComputeCrc32(data);
    }

    /// <summary>
    /// Compute CRC32 of byte data.
    /// </summary>
    public static uint ComputeCrc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc >> 1) ^ (0xEDB88320 & (uint)-(crc & 1));
            }
        }
        return ~crc;
    }
}

/// <summary>
/// Represents parsed chunk data from a binary message.
/// </summary>
public class ChunkData
{
    public int ChunkIndex { get; set; }
    public uint TransferIdCrc { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}