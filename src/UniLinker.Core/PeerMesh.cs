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
    /// Register a PeerConnection for an incoming SDP offer (from a browser or another device).
    /// Called by SignalingServer when it receives an offer.
    /// The caller should have already called InitializeAsync and SetRemoteDescriptionAsync
    /// on this PeerConnection before registering.
    /// </summary>
    public PeerConnection RegisterIncomingConnection(PeerInfo peer)
    {
        lock (_lock)
        {
            if (!_peers.ContainsKey(peer.Id))
                _peers[peer.Id] = peer;

            if (_connections.TryGetValue(peer.Id, out var existing))
            {
                return existing;
            }

            var pc = new PeerConnection(peer);
            WireConnectionEvents(pc, peer);
            _connections[peer.Id] = pc;
            return pc;
        }
    }

    /// <summary>
    /// Create a channel to a remote peer for a given capability.
    ///
    /// INCOMING path (sender/host): Connection was pre-registered by SignalingServer
    ///   with SDP offer already handled. We just create the channel wrapper.
    ///
    /// OUTGOING path (watcher): Create new PeerConnection, perform SDP exchange
    ///   via HTTP to the remote peer's signaling server.
    /// </summary>
    public async Task<IChannel?> CreateChannel(
        PeerInfo peer, string capability, ChannelOptions? options = null)
    {
        PeerConnection pc;
        bool isSender;

        lock (_lock)
        {
            if (_connections.TryGetValue(peer.Id, out var existing))
            {
                // INCOMING path: connection was pre-registered by SignalingServer.
                pc = existing;
                isSender = true;
            }
            else
            {
                // OUTGOING path: create a new connection, will do SDP exchange.
                pc = new PeerConnection(peer);
                WireConnectionEvents(pc, peer);
                _connections[peer.Id] = pc;
                _peers[peer.Id] = peer;
                isSender = false;
            }
        }

        // OUTGOING (watcher) path: perform WebRTC SDP exchange via HTTP
        if (!isSender)
        {
            var initialized = await pc.InitializeAsync();
            if (!initialized)
            {
                RemoveConnection(peer.Id);
                pc.Dispose();
                return null;
            }

            var success = await PerformWebRtcExchangeAsync(pc, peer, capability);
            if (!success)
            {
                RemoveConnection(peer.Id);
                pc.Dispose();
                return null;
            }
        }

        var channel = new PeerChannel(peer, capability, pc, isSender);
        await channel.OpenAsync();
        return channel;
    }

    /// <summary>
    /// Perform WebRTC SDP exchange: create offer, POST to remote signaling,
    /// receive answer, set remote description.
    /// </summary>
    private async Task<bool> PerformWebRtcExchangeAsync(
        PeerConnection pc, PeerInfo peer, string capability)
    {
        try
        {
            // Generate SDP offer
            var offerSdp = await pc.CreateOfferAsync();

            // Build signaling message
            var msg = new SignalingMessage
            {
                Type = "offer",
                Sdp = offerSdp,
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
                    $"[PeerMesh] WebRTC exchange failed: HTTP {(int)response.StatusCode}");
                return false;
            }

            var answerJson = await response.Content.ReadAsStringAsync();
            var answer = JsonSerializer.Deserialize<SignalingMessage>(answerJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });

            if (answer == null || string.IsNullOrEmpty(answer.Sdp))
            {
                System.Diagnostics.Debug.WriteLine("[PeerMesh] WebRTC exchange failed: empty answer");
                return false;
            }

            // Set remote SDP (answer)
            await pc.SetRemoteDescriptionAsync(answer.Sdp, isOffer: false);

            System.Diagnostics.Debug.WriteLine(
                $"[PeerMesh] WebRTC exchange complete for peer {peer.Name}");

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PeerMesh] WebRTC exchange error: {ex.Message}");
            return false;
        }
    }

    private void RemoveConnection(string peerId)
    {
        lock (_lock)
        {
            _connections.Remove(peerId);
            _peers.Remove(peerId);
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
                PeerDisconnected?.Invoke(peer);
                RemoveConnection(peer.Id);
            }
        };
    }

    public async Task DisconnectPeer(PeerInfo peer)
    {
        PeerConnection? pc;
        lock (_lock)
        {
            _connections.TryGetValue(peer.Id, out pc);
        }
        if (pc != null)
        {
            await pc.CloseAsync();
        }
        RemoveConnection(peer.Id);
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
/// Wraps a PeerConnection as an IChannel for WebRTC media streaming.
/// </summary>
internal class PeerChannel : IChannel
{
    private readonly PeerConnection _pc;
    private readonly bool _isSender;

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
            // Receiver side: listen for incoming RTP packets
            // In WebRTC mode, the browser handles decoding; we forward raw data
            _pc.IceCandidatesGenerated += OnIceCandidates;
        }
    }

    private void OnIceCandidates(List<SIPSorcery.Net.RTCIceCandidate> candidates)
    {
        // ICE candidates are handled at the signaling level,
        // but we can log them here for debugging.
        foreach (var c in candidates)
        {
            System.Diagnostics.Debug.WriteLine($"[Channel:{Id}] ICE candidate: {c}");
        }
    }

    public async Task OpenAsync()
    {
        if (!_isSender)
        {
            // Receiver side: initialize and wait for connection
            var ok = await _pc.InitializeAsync();
            if (!ok)
            {
                State = ChannelState.Closed;
                return;
            }
        }
        // For sender, the connection was already initialized by SignalingServer.
        State = ChannelState.Open;
    }

    public Task SendAsync(byte[] data) =>
        throw new NotSupportedException("MediaTrack channels use SendPacketAsync");

    public async Task SendPacketAsync(EncodedPacket packet)
    {
        if (State == ChannelState.Open && _isSender)
        {
            // Convert timestamp from microseconds to RTP timestamp units (90kHz clock)
            var rtpTimestamp = (uint)(packet.TimestampUs * 90 / 1000);
            _pc.SendVideoFrame(rtpTimestamp, packet.Data);
        }
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (!_isSender)
            _pc.IceCandidatesGenerated -= OnIceCandidates;
        State = ChannelState.Closed;
        OnClose?.Invoke();
    }
}
