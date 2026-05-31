using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

public class PeerMesh : IPeerMesh, IDisposable
{
    private readonly Dictionary<string, PeerConnection> _connections = new();
    private readonly Dictionary<string, PeerInfo> _peers = new();
    private readonly object _lock = new();

    public IReadOnlyList<PeerInfo> ConnectedPeers
    {
        get
        {
            lock (_lock)
            {
                return _peers.Values.ToList().AsReadOnly();
            }
        }
    }

    public event Action<PeerInfo>? PeerConnected;
    public event Action<PeerInfo>? PeerDisconnected;
    public event Func<PeerInfo, string, Task<IChannel?>>? ChannelRequested;

    /// <summary>
    /// Raise ChannelRequested from external code (e.g. SignalingServer).
    /// C# events can only be invoked from the declaring class.
    /// </summary>
    public async Task<IChannel?> RaiseChannelRequestedAsync(PeerInfo peer, string capability)
    {
        var handler = ChannelRequested;
        if (handler != null)
        {
            var results = await Task.WhenAll(
                handler.GetInvocationList()
                    .Cast<Func<PeerInfo, string, Task<IChannel?>>>()
                    .Select(d => d(peer, capability)));
            return results.FirstOrDefault(r => r != null);
        }
        return null;
    }

    /// <summary>
    /// Register a PeerConnection for an incoming SDP offer.
    /// The remote endpoint (IP:port from the offer) is already set on the connection.
    /// Called by SignalingServer when it receives an offer.
    /// </summary>
    public PeerConnection RegisterIncomingConnection(PeerInfo peer, string remoteIp, int remotePort)
    {
        lock (_lock)
        {
            if (!_peers.ContainsKey(peer.Id))
                _peers[peer.Id] = peer;

            if (_connections.TryGetValue(peer.Id, out var existing))
            {
                // Update remote endpoint on existing connection
                existing.SetRemoteEndPoint(remoteIp, remotePort);
                return existing;
            }

            var pc = new PeerConnection(peer);
            pc.SetRemoteEndPoint(remoteIp, remotePort);
            WireConnectionEvents(pc, peer);
            _connections[peer.Id] = pc;
            return pc;
        }
    }

    /// <summary>
    /// Create a channel to a remote peer.
    ///
    /// Two scenarios:
    /// 1. OUTGOING (watcher): No existing connection — creates offer, HTTP POSTs to peer's
    ///    signaling server, gets answer, sets remote endpoint, starts receiving.
    /// 2. INCOMING (sender): Connection was already registered by SignalingServer with
    ///    remote endpoint set — just creates the channel wrapper and starts sending.
    /// </summary>
    public async Task<IChannel?> CreateChannel(
        PeerInfo peer, string capability, ChannelOptions? options = null)
    {
        PeerConnection pc;
        bool isSender; // true = we send media (screen capture host), false = we receive (watcher)

        lock (_lock)
        {
            if (_connections.TryGetValue(peer.Id, out var existing))
            {
                // INCOMING path: connection was pre-registered by SignalingServer.
                // We are the SENDER (host sharing screen).
                pc = existing;
                isSender = true;
            }
            else
            {
                // OUTGOING path: create a new connection, will do SDP exchange.
                // We are the RECEIVER (watcher).
                pc = new PeerConnection(peer);
                WireConnectionEvents(pc, peer);
                _connections[peer.Id] = pc;
                _peers[peer.Id] = peer;
                isSender = false;
            }
        }

        // OUTGOING (watcher) path: perform SDP exchange via HTTP
        if (!isSender)
        {
            var success = await PerformSdpExchangeAsync(pc, peer, capability);
            if (!success)
            {
                lock (_lock)
                {
                    _connections.Remove(peer.Id);
                    _peers.Remove(peer.Id);
                }
                pc.Dispose();
                return null;
            }
        }

        var channel = new PeerChannel(peer, capability, pc, isSender);
        await channel.OpenAsync();
        return channel;
    }

    /// <summary>
    /// Send an SDP offer to the remote peer's signaling server,
    /// receive the answer, and parse the remote endpoint.
    /// </summary>
    private async Task<bool> PerformSdpExchangeAsync(
        PeerConnection pc, PeerInfo peer, string capability)
    {
        try
        {
            // Generate SDP offer with our local RTP port
            var offer = pc.CreateOffer();

            // Build signaling message
            var msg = new SignalingMessage
            {
                Type = "offer",
                Sdp = offer,
                FromPeerId = GetLocalDeviceId(),
                Capability = capability,
            };
            var json = JsonSerializer.Serialize(msg, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });

            // POST to remote peer's signaling server
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"http://{peer.IpAddress}:{peer.Port}/signaling";

            System.Diagnostics.Debug.WriteLine(
                $"[PeerMesh] Sending offer to {url} for capability '{capability}'");

            var response = await http.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PeerMesh] SDP exchange failed: HTTP {(int)response.StatusCode}");
                return false;
            }

            var answerJson = await response.Content.ReadAsStringAsync();
            var answer = JsonSerializer.Deserialize<SignalingMessage>(answerJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });

            if (answer == null || string.IsNullOrEmpty(answer.Sdp))
            {
                System.Diagnostics.Debug.WriteLine("[PeerMesh] SDP exchange failed: empty answer");
                return false;
            }

            // Parse the answer to extract remote RTP port
            pc.ParseRemoteSdp(answer.Sdp);

            if (pc.RemoteEndPoint == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[PeerMesh] SDP exchange failed: could not parse remote endpoint from answer");
                return false;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[PeerMesh] SDP exchange complete: remote={pc.RemoteEndPoint}, localRtpPort={pc.LocalRtpPort}");

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PeerMesh] SDP exchange error: {ex.Message}");
            return false;
        }
    }

    private void WireConnectionEvents(PeerConnection pc, PeerInfo peer)
    {
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
                lock (_lock)
                {
                    _peers.Remove(peer.Id);
                    _connections.Remove(peer.Id);
                }
                PeerDisconnected?.Invoke(peer);
            }
        };
    }

    public async Task DisconnectPeer(PeerInfo peer)
    {
        lock (_lock)
        {
            if (_connections.TryGetValue(peer.Id, out var pc))
            {
                pc.Dispose();
                _connections.Remove(peer.Id);
                _peers.Remove(peer.Id);
            }
        }
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var pc in _connections.Values)
                pc.Dispose();
            _connections.Clear();
            _peers.Clear();
        }
    }

    private static string GetLocalDeviceId()
    {
        try
        {
            return Environment.MachineName;
        }
        catch
        {
            return "unknown";
        }
    }
}

/// <summary>
/// Wraps a PeerConnection as an IChannel for media streaming.
/// </summary>
internal class PeerChannel : IChannel
{
    private readonly PeerConnection _pc;
    private readonly bool _isSender;
    private readonly RtpDepacketizer? _depacketizer;

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public ChannelType Type { get; }
    public PeerInfo RemotePeer { get; }
    public string Capability { get; }
    public ChannelState State { get; private set; } = ChannelState.Connecting;
    public bool IsOpen => State == ChannelState.Open;

    public event Action? OnClose;
#pragma warning disable CS0067
    public event Action<byte[]>? MessageReceived;
#pragma warning restore CS0067
    public event Action<EncodedPacket>? PacketReceived;

    public PeerChannel(PeerInfo remotePeer, string capability, PeerConnection pc, bool isSender)
    {
        RemotePeer = remotePeer;
        Capability = capability;
        Type = ChannelType.MediaTrack;
        _pc = pc;
        _isSender = isSender;

        if (!isSender)
        {
            // Receiver side: set up depacketizer and forward decoded packets
            _depacketizer = new RtpDepacketizer();
            _pc.RtpPacketReceived += OnRtpPacketReceived;
        }
    }

    private void OnRtpPacketReceived(byte[] rtpPacket)
    {
        var nal = _depacketizer?.ProcessPacket(rtpPacket);
        if (nal != null)
        {
            // Fire as an EncodedPacket for consumers
            var packet = new EncodedPacket(nal, DateTimeOffset.UtcNow.Ticks, IsKeyFrame(nal));
            PacketReceived?.Invoke(packet);
        }
    }

    private static bool IsKeyFrame(byte[] nal)
    {
        if (nal.Length == 0) return false;
        var nalType = nal[0] & 0x1F;
        // Type 5 = IDR slice (keyframe), Type 7 = SPS, Type 8 = PPS
        return nalType == 5 || nalType == 7 || nalType == 8;
    }

    public async Task OpenAsync()
    {
        if (_isSender)
        {
            await _pc.StartSendingAsync();
        }
        else
        {
            // Start receiving on the local RTP port chosen during CreateOffer
            _pc.StartReceiving(_pc.LocalRtpPort);
        }
        State = ChannelState.Open;
    }

    public Task SendAsync(byte[] data) =>
        throw new NotSupportedException("MediaTrack channels use SendPacketAsync");

    public async Task SendPacketAsync(EncodedPacket packet)
    {
        if (State == ChannelState.Open && _isSender)
            await _pc.SendEncodedPacketAsync(packet);
    }

    public void Dispose()
    {
        if (!_isSender)
            _pc.RtpPacketReceived -= OnRtpPacketReceived;
        State = ChannelState.Closed;
        OnClose?.Invoke();
    }
}
