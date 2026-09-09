namespace UniLinker.Plugin.Sdk;

/// <summary>
/// A communication channel to a remote peer, backed by a WebRTC DataChannel or MediaTrack.
/// Plugins use channels to send/receive data (encoded packets or raw bytes).
/// </summary>
public interface IChannel : IDisposable
{
    /// <summary>Unique channel identifier.</summary>
    string Id { get; }

    /// <summary>Whether this is a MediaTrack (video/audio) or DataChannel (reliable data).</summary>
    ChannelType Type { get; }

    /// <summary>Information about the remote peer on the other end.</summary>
    PeerInfo RemotePeer { get; }

    /// <summary>The capability this channel serves (e.g., "screen-capture").</summary>
    string Capability { get; }

    /// <summary>Current lifecycle state of the channel.</summary>
    ChannelState State { get; }

    /// <summary>Convenience check: true if State is Open.</summary>
    bool IsOpen { get; }

    /// <summary>Raised when the channel is closed (by local or remote side).</summary>
    event Action? OnClose;

    /// <summary>Raised when a raw byte message is received on a DataChannel.</summary>
    event Action<byte[]>? MessageReceived;

    /// <summary>Send raw byte data over a DataChannel.</summary>
    Task SendAsync(byte[] data);

    /// <summary>Raised when an encoded media packet is received on a MediaTrack.</summary>
    event Action<EncodedPacket>? PacketReceived;

    /// <summary>Send an encoded media packet (H.264 NAL) over a MediaTrack.</summary>
    Task SendPacketAsync(EncodedPacket packet);
}
