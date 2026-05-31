namespace UniLinker.Plugin.Sdk;

public interface IChannel : IDisposable
{
    string Id { get; }
    ChannelType Type { get; }
    PeerInfo RemotePeer { get; }
    string Capability { get; }
    ChannelState State { get; }
    bool IsOpen { get; }
    event Action? OnClose;
    event Action<byte[]>? MessageReceived;
    Task SendAsync(byte[] data);
    event Action<EncodedPacket>? PacketReceived;
    Task SendPacketAsync(EncodedPacket packet);
}
