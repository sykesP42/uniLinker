namespace UniLinker.Plugin.Sdk;

public interface IPeerMesh
{
    IReadOnlyList<PeerInfo> ConnectedPeers { get; }
    event Action<PeerInfo>? PeerConnected;
    event Action<PeerInfo>? PeerDisconnected;
    event Func<PeerInfo, string, Task<IChannel?>>? ChannelRequested;
    Task<IChannel?> CreateChannel(PeerInfo peer, string capability, ChannelOptions? options = null);
    Task DisconnectPeer(PeerInfo peer);
}
