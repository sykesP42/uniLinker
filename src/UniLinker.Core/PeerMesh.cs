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
    /// Raised when a remote peer opened a DataChannel on an incoming
    /// connection (e.g. a file transfer pushed to this device).
    /// </summary>
    public event Action<PeerInfo, IChannel>? DataChannelReady;

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
    /// Register a pre-negotiated PeerConnection for an incoming SDP offer
    /// (from a browser or another device). Called by SignalingServer with the
    /// exact connection that received the offer, so plugin channels are bound
    /// to the live WebRTC session.
    /// </summary>
    public PeerConnection RegisterIncomingConnection(PeerInfo peer, PeerConnection connection)
    {
        lock (_lock)
        {
            if (!_peers.ContainsKey(peer.Id))
                _peers[peer.Id] = peer;

            if (_connections.TryGetValue(peer.Id, out var existing))
            {
                if (!ReferenceEquals(existing, connection))
                {
                    existing.Dispose();
                    _connections[peer.Id] = connection;
                }
                return _connections[peer.Id];
            }

            WireConnectionEvents(connection, peer);
            _connections[peer.Id] = connection;
            return connection;
        }
    }

    /// <summary>
    /// Register a connection for an incoming offer without an existing
    /// PeerConnection (creates a fresh, un-negotiated one — the caller must
    /// have handled the SDP offer itself).
    /// </summary>
    public PeerConnection RegisterIncomingConnection(PeerInfo peer)
    {
        return RegisterIncomingConnection(peer, new PeerConnection(peer));
    }

    /// <summary>
    /// Create a channel to a remote peer for a given capability.
    ///
    /// INCOMING path (sender/host): Connection was pre-registered by SignalingServer
    ///   with SDP offer already handled. We just create the channel wrapper.
    ///
    /// OUTGOING path (watcher): Create new PeerConnection, perform SDP exchange
    ///   via HTTP to the remote peer's signaling server.
    ///
    /// For DataChannel type, creates a reliable data channel instead of media track.
    /// </summary>
    public async Task<IChannel?> CreateChannel(
        PeerInfo peer, string capability, ChannelOptions? options = null)
    {
        // Check if DataChannel is requested
        if (options?.Type == ChannelType.DataChannel)
        {
            return await CreateDataChannelAsync(peer, capability, options);
        }

        // Default: MediaTrack channel
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
    /// Create a DataChannel for reliable data transfer (e.g., file transfer).
    /// Uses the same WebRTC connection as media, but creates a separate data channel.
    /// </summary>
    private async Task<IChannel?> CreateDataChannelAsync(
        PeerInfo peer, string capability, ChannelOptions options)
    {
        PeerConnection pc;

        lock (_lock)
        {
            if (!_connections.TryGetValue(peer.Id, out var existing))
            {
                // Create new connection for DataChannel
                pc = new PeerConnection(peer);
                WireConnectionEvents(pc, peer);
                _connections[peer.Id] = pc;
                _peers[peer.Id] = peer;
            }
            else
            {
                pc = existing;
            }
        }

        // Initialize connection if needed
        if (pc.State == PeerConnectionState.New)
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

        // Wait for connection to be established
        var timeout = TimeSpan.FromSeconds(10);
        var startTime = DateTime.UtcNow;
        while (pc.State != PeerConnectionState.Connected && DateTime.UtcNow - startTime < timeout)
        {
            await Task.Delay(100);
        }

        if (pc.State != PeerConnectionState.Connected)
        {
            System.Diagnostics.Debug.WriteLine($"[PeerMesh] DataChannel creation failed: not connected");
            return null;
        }

        // Create the DataChannel
        var label = options.Label ?? $"data-{capability}";
        var dc = await pc.CreateDataChannelAsync(label, options.Ordered);
        if (dc == null)
        {
            System.Diagnostics.Debug.WriteLine($"[PeerMesh] Failed to create DataChannel");
            return null;
        }

        // Wait for DataChannel to open
        var dcOpenTcs = new TaskCompletionSource<bool>();
        dc.onopen += () => dcOpenTcs.TrySetResult(true);
        dc.onerror += (err) => dcOpenTcs.TrySetResult(false);

        var dcTimeout = Task.Delay(5000);
        var winner = await Task.WhenAny(dcOpenTcs.Task, dcTimeout);
        if (winner == dcTimeout || !await dcOpenTcs.Task)
        {
            System.Diagnostics.Debug.WriteLine($"[PeerMesh] DataChannel open timeout");
            return null;
        }

        return new DataChannelWrapper(dc, peer, capability);
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

        pc.DataChannelReceived += channel =>
        {
            try
            {
                var wrapper = new DataChannelWrapper(channel, peer, channel.label);
                DataChannelReady?.Invoke(peer, wrapper);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PeerMesh] DataChannel wrap failed: {ex.Message}");
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

    /// <summary>Capture timestamp of the last frame sent on this channel, in
    /// microseconds. -1 means "nothing sent yet".</summary>
    private long _lastFrameTsUs = -1;

    /// <summary>Duration of the previous frame in 90 kHz RTP units. Doubles as
    /// the fallback when a timestamp fails to advance, so a stalled clock can
    /// never collapse the stream's time base.</summary>
    private uint _lastDurationRtp = 90000 / 30;

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
            // SIPSorcery's SendVideo takes a DURATION in RTP units (added to the
            // previous timestamp), not an absolute timestamp. Derive that duration
            // from the real capture timestamps so the 90 kHz clock tracks the
            // actual frame cadence.
            //
            // A fixed 90000/30 (the previous implementation) makes a 60 fps stream
            // advance at half speed: the receiver reconstructs 30 fps worth of
            // time per second and discards roughly every other frame, while its
            // jitter estimate grows monotonically. Deriving the delta from
            // TimestampUs keeps 30/60/120 fps all correct.
            uint durationRtp;
            if (_lastFrameTsUs < 0)
            {
                // First frame: no delta available, assume the nominal 30 fps.
                durationRtp = _lastDurationRtp;
            }
            else
            {
                long deltaUs = packet.TimestampUs - _lastFrameTsUs;
                if (deltaUs <= 0)
                {
                    // Timestamp did not advance (idle re-emit of a cached frame).
                    // Repeat the previous duration instead of emitting zero, which
                    // would freeze the receiver's playout clock.
                    durationRtp = _lastDurationRtp;
                }
                else
                {
                    // microseconds -> 90 kHz ticks: us * 90000 / 1_000_000 == us * 9 / 100
                    long ticks = deltaUs * 9 / 100;
                    // Clamp to 1 fps .. 120 fps so a bad timestamp cannot emit an
                    // absurd gap.
                    durationRtp = (uint)Math.Clamp(ticks, 750, 90000);
                }
            }

            _lastFrameTsUs = packet.TimestampUs;
            _lastDurationRtp = durationRtp;

            _pc.SendVideoFrame(durationRtp, packet.Data);
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
