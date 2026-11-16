using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

/// <summary>
/// Transport-free signaling business logic, extracted from SignalingServer
/// during the M2 Kestrel migration (docs/spec/M2-kestrel.md). The HTTP layer
/// (SignalingKestrelHost) is a pure mapping on top; JSON wire shapes are
/// locked by SignalingContractTests + E2E.
///
/// The wire contract this must preserve (legacy TcpListener server):
///   HandleOfferAsync  — build a PeerConnection for an offer, filter loopback
///                       candidates, register the SAME instance in the mesh,
///                       wait up to 3s for ICE gathering (answer embeds our
///                       candidates), fire channel requests, return the
///                       answer SignalingMessage (Capability stays unset).
///   HandleIceCandidateAsync — feed a remote candidate into the pending PC,
///                       silently ignoring unknown peers.
/// </summary>
public class SignalingService : IDisposable
{
    private readonly int _port;
    private readonly PeerMesh _peerMesh;
    private readonly string[] _capabilities;
    private readonly object _pendingIceLock = new();
    private readonly Dictionary<string, PeerConnection> _pendingIceConnections = new();

    public SignalingService(
        PeerMesh peerMesh,
        int port,
        string[]? capabilities = null)
    {
        _peerMesh = peerMesh;
        _port = port;
        _capabilities = capabilities ?? ["screen-capture", "h264-encode", "video-stream"];
    }

    public int Port => _port;

    /// <summary>GET /info — device metadata and capabilities.</summary>
    public object GetInfo() => new
    {
        name = GetDeviceName(),
        version = UniLinkerInfo.Version,
        capabilities = _capabilities,
        status = "available",
    };

    /// <summary>GET /api/status — runtime status; running state is owned by
    /// the transport host, not the service.</summary>
    public object GetStatus(bool isRunning) => new
    {
        deviceName = GetDeviceName(),
        version = UniLinkerInfo.Version,
        port = _port,
        peers = _peerMesh.ConnectedPeers.Count,
        status = isRunning ? "running" : "stopped",
    };

    /// <summary>
    /// Handle an SDP offer: create and register the peer connection, wait for
    /// ICE gathering, and return the answer. Returns null when the WebRTC
    /// stack failed to initialize (transport maps this to 500).
    /// </summary>
    public async Task<SignalingMessage?> HandleOfferAsync(SignalingMessage msg)
    {
        var peerId = string.IsNullOrEmpty(msg.FromPeerId) ? "unknown" : msg.FromPeerId;
        var peerInfo = new PeerInfo
        {
            Id = peerId,
            Name = peerId,
            IpAddress = "0.0.0.0", // filled in by the transport from the socket
            Port = _port,
            State = PeerState.Discovered,
            Capabilities = msg.Capability != null ? [msg.Capability] : [],
        };

        var pc = new PeerConnection(peerInfo);
        if (!await pc.InitializeAsync())
        {
            return null;
        }

        // Strip loopback candidates from the remote offer: libwebrtc includes
        // 127.0.0.1/::1 host candidates, and SIPSorcery's ICE pairs them
        // (loopback-to-loopback across machines never works) while starving
        // the viable pairs - the observed CHECKING->FAILED with zero packets.
        var offerSdp = FilterLoopbackCandidates(msg.Sdp);

        pc.SetRemoteDescriptionAsync(offerSdp, isOffer: true);

        // Register the SAME connection in the mesh so plugin channels are
        // bound to the negotiated peer connection, not a fresh dead one.
        _peerMesh.RegisterIncomingConnection(peerInfo, pc);

        lock (_pendingIceLock)
        {
            _pendingIceConnections[peerId] = pc;
        }

        pc.StateChanged += state =>
        {
            if (state is PeerConnectionState.Closed or PeerConnectionState.Failed)
            {
                lock (_pendingIceLock)
                {
                    _pendingIceConnections.Remove(peerId);
                }
            }
        };

        // Wait for ICE gathering so the answer SDP carries our candidates.
        await pc.WaitForIceGatheringAsync(TimeSpan.FromSeconds(3));

        // Fire the channel request so the owning plugin starts producing.
        if (!string.IsNullOrEmpty(msg.Capability))
        {
            _ = _peerMesh.RaiseChannelRequestedAsync(peerInfo, msg.Capability);
        }
        else
        {
            foreach (var cap in _capabilities)
            {
                _ = _peerMesh.RaiseChannelRequestedAsync(peerInfo, cap);
            }
        }

        // Strip our own loopback candidates from the answer for the same
        // reason - the peer would starve the real pairs otherwise.
        var localSdp = FilterLoopbackCandidates(pc.GetLocalSdp() ?? "");
        Log.Info($"Offer handled for '{peerId}' capability={msg.Capability} answerSdpBytes={localSdp.Length} offerBytes={msg.Sdp.Length}");

        return new SignalingMessage
        {
            Type = "answer",
            Sdp = localSdp,
            FromPeerId = GetDeviceName(),
        };
    }

    /// <summary>POST /ice — feed a remote ICE candidate into the pending peer
    /// connection. Unknown peers are silently ignored (legacy behavior).</summary>
    public Task HandleIceCandidateAsync(IceCandidateMessage msg)
    {
        if (string.IsNullOrEmpty(msg.PeerId)) return Task.CompletedTask;

        lock (_pendingIceLock)
        {
            if (_pendingIceConnections.TryGetValue(msg.PeerId, out var pc))
            {
                pc.AddIceCandidate(new SIPSorcery.Net.RTCIceCandidateInit
                {
                    candidate = msg.Candidate ?? "",
                    sdpMid = msg.SdpMid ?? "0",
                    sdpMLineIndex = (ushort)msg.SdpMLineIndex,
                });
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Stop: dispose all pending negotiated connections (mirrors the
    /// legacy server's Stop behavior).</summary>
    public void Stop()
    {
        lock (_pendingIceLock)
        {
            foreach (var pc in _pendingIceConnections.Values)
                pc.Dispose();
            _pendingIceConnections.Clear();
        }
    }

    public void Dispose() => Stop();

    // Test hooks (internal; InternalsVisibleTo → UniLinker.Core.Tests).
    internal int PendingIceCount
    {
        get { lock (_pendingIceLock) return _pendingIceConnections.Count; }
    }

    internal IReadOnlyCollection<string> PeerIds
    {
        get { lock (_pendingIceLock) return _pendingIceConnections.Keys.ToList(); }
    }

    /// <summary>Get the device display name; never fails.</summary>
    private static string GetDeviceName()
    {
        try { return Environment.MachineName; }
        catch { return "unknown"; }
    }

    /// <summary>
    /// Remove loopback host candidates (127.0.0.1/::1) from an SDP. Cross-machine
    /// loopback-to-loopback ICE pairs never work and starve the viable pairs -
    /// the observed CHECKING->FAILED with zero STUN packets. Normalizes CR/LF.
    /// </summary>
    internal static string FilterLoopbackCandidates(string sdp)
    {
        var lines = sdp.Replace("\r\n", "\n").Split('\n');
        var kept = lines
            .Where(l => !l.TrimStart().StartsWith("a=candidate:") ||
                        (!l.Contains("127.0.0.1") && !l.Contains("::1")))
            .ToList();
        return string.Join("\r\n", kept);
    }
}
