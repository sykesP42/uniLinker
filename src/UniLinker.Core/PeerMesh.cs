using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class PeerMesh : IPeerMesh, IDisposable
{
    private readonly Dictionary<string, PeerConnection> _connections = new();
    private readonly Dictionary<string, PeerInfo> _peers = new();

    public IReadOnlyList<PeerInfo> ConnectedPeers => _peers.Values.ToList().AsReadOnly();
    public event Action<PeerInfo>? PeerConnected;
    public event Action<PeerInfo>? PeerDisconnected;
#pragma warning disable CS0067
    public event Func<PeerInfo, string, Task<IChannel?>>? ChannelRequested;
#pragma warning restore CS0067

    public async Task<IChannel?> CreateChannel(
        PeerInfo peer, string capability, ChannelOptions? options = null)
    {
        if (!_connections.TryGetValue(peer.Id, out var pc))
        {
            pc = new PeerConnection(peer);
            pc.StateChanged += state =>
            {
                if (state == PeerConnectionState.Connected)
                {
                    peer.State = PeerState.Connected;
                    PeerConnected?.Invoke(peer);
                }
                else if (state is PeerConnectionState.Closed or PeerConnectionState.Failed)
                {
                    peer.State = PeerState.Disconnected;
                    _peers.Remove(peer.Id);
                    _connections.Remove(peer.Id);
                    PeerDisconnected?.Invoke(peer);
                }
            };
            _connections[peer.Id] = pc;
            _peers[peer.Id] = peer;
        }

        var channel = new PeerChannel(peer, capability, pc);
        await channel.OpenAsync();
        return channel;
    }

    public async Task DisconnectPeer(PeerInfo peer)
    {
        if (_connections.TryGetValue(peer.Id, out var pc))
        {
            pc.Dispose();
            _connections.Remove(peer.Id);
            _peers.Remove(peer.Id);
        }
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var pc in _connections.Values)
            pc.Dispose();
        _connections.Clear();
        _peers.Clear();
    }
}

/// <summary>
/// Wraps a PeerConnection as an IChannel for media streaming.
/// </summary>
internal class PeerChannel : IChannel
{
    private readonly PeerConnection _pc;

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public ChannelType Type { get; }
    public PeerInfo RemotePeer { get; }
    public string Capability { get; }
    public ChannelState State { get; private set; } = ChannelState.Connecting;
    public bool IsOpen => State == ChannelState.Open;
    public event Action? OnClose;
#pragma warning disable CS0067
    public event Action<byte[]>? MessageReceived;
    public event Action<EncodedPacket>? PacketReceived;
#pragma warning restore CS0067

    public PeerChannel(PeerInfo remotePeer, string capability, PeerConnection pc)
    {
        RemotePeer = remotePeer;
        Capability = capability;
        Type = ChannelType.MediaTrack;
        _pc = pc;
    }

    public async Task OpenAsync()
    {
        await _pc.StartSendingAsync();
        State = ChannelState.Open;
    }

    public Task SendAsync(byte[] data) =>
        throw new NotSupportedException("MediaTrack channels use SendPacketAsync");

    public async Task SendPacketAsync(EncodedPacket packet)
    {
        if (State == ChannelState.Open)
            await _pc.SendEncodedPacketAsync(packet);
    }

    public void Dispose()
    {
        State = ChannelState.Closed;
        OnClose?.Invoke();
    }
}
