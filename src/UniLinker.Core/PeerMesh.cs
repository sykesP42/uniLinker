using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class PeerMesh : IPeerMesh, IDisposable
{
    private readonly List<PeerInfo> _peers = new();

    public IReadOnlyList<PeerInfo> ConnectedPeers => _peers.AsReadOnly();
#pragma warning disable CS0067
    public event Action<PeerInfo>? PeerConnected;
    public event Action<PeerInfo>? PeerDisconnected;
    public event Func<PeerInfo, string, Task<IChannel?>>? ChannelRequested;
#pragma warning restore CS0067

    public Task<IChannel?> CreateChannel(
        PeerInfo peer, string capability, ChannelOptions? options = null)
    {
        return Task.FromResult<IChannel?>(null);
    }

    public Task DisconnectPeer(PeerInfo peer) => Task.CompletedTask;

    public void Dispose() { }
}
