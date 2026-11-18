using System.Net;
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

    /// <summary>
    /// Handle a direct-RTP session request (M3, /signaling with type "rtp"):
    /// build a DirectRtpChannel toward the requester's UDP listener, register
    /// it so the plugin's viewer flow picks it up, and return the answer the
    /// Android side needs (our SDES crypto line + RTP bind port + SSRC).
    /// Returns null when rtpPort is missing (transport → 400).
    /// </summary>
    public async Task<RtpSessionAnswer?> HandleRtpSessionAsync(
        SignalingMessage msg, IPAddress remoteIp, int bindPort = DirectRtpChannel.DefaultRtpPort)
    {
        if (msg.RtpPort <= 0) return null;

        var peerId = string.IsNullOrEmpty(msg.FromPeerId) ? "unknown" : msg.FromPeerId;
        var peerInfo = new PeerInfo
        {
            Id = peerId,
            Name = peerId,
            IpAddress = remoteIp.ToString(),
            Port = msg.RtpPort,
            State = PeerState.Discovered,
            Capabilities = [msg.Capability ?? "screen-capture"],
        };

        // Same peer re-requesting (app restart, reconnect): dispose the stale
        // channel so its RTP bind port is free for the new session. The
        // dispose chain (OnClose -> plugin StopSession -> encoder join) once
        // blocked this request thread for 10+s and timed the tablet out —
        // bounded to 3s here, then we proceed anyway: the sender's UDP bind
        // uses ReuseAddress, so the brief overlap is survivable.
        var staleChannel = _peerMesh.RemoveDirectChannel(peerId);
        if (staleChannel != null)
        {
            try
            {
                await Task.Run(() => staleChannel.Dispose())
                    .WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                Log.Info($"stale channel dispose for '{peerId}' did not finish in 3s: {ex.Message}");
            }
        }

        IDirectRtpChannel channel = DirectRtpTransport.Mode switch
        {
            "rtx" => new DirectRtpChannel(
                peerInfo, msg.Capability ?? "screen-capture",
                new IPEndPoint(remoteIp, msg.RtpPort), bindPort,
                msg.CaptureWidth, msg.CaptureHeight, msg.BitrateKbps, msg.CaptureFps),
            _ => new DirectRtpChannelLegacy(
                peerInfo, msg.Capability ?? "screen-capture",
                new IPEndPoint(remoteIp, msg.RtpPort), bindPort,
                msg.CaptureWidth, msg.CaptureHeight, msg.BitrateKbps, msg.CaptureFps),
        };
        try
        {
            await channel.OpenAsync();
        }
        catch (Exception ex)
        {
            channel.Dispose();
            Log.Info($"Direct RTP session for '{peerId}' failed: {ex.Message}");
            throw; // transport → 500; e.g. bind port already in use (E_BUSY)
        }

        _peerMesh.RegisterDirectChannel(peerInfo, channel);
        _ = _peerMesh.RaiseChannelRequestedAsync(peerInfo, channel.Capability);

        return new RtpSessionAnswer(
            Type: "rtp-answer",
            Crypto: channel.CryptoLine,
            Ssrc: channel.Ssrc,
            RtpPort: bindPort);
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

/// <summary>
/// /signaling wire DTO. Serialized AND deserialized with
/// JsonNamingPolicy.CamelCase; shapes are locked by SignalingContractTests
/// (both the Android app and the browser viewer depend on them).
/// </summary>
public class SignalingMessage
{
    public string Type { get; set; } = ""; // "offer", "answer" or "rtp"
    public string Sdp { get; set; } = "";
    public string FromPeerId { get; set; } = "";
    public string? Capability { get; set; }

    /// <summary>type=="rtp": the requester's UDP listening port (M3 direct tier).</summary>
    public int RtpPort { get; set; }

    /// <summary>type=="rtp": receiver-driven capture tier (0 = sender default).</summary>
    public int CaptureWidth { get; set; }
    public int CaptureHeight { get; set; }
    public int BitrateKbps { get; set; }
    public int CaptureFps { get; set; }
}

/// <summary>/ice wire DTO (camelCase on the wire).</summary>
public class IceCandidateMessage
{
    public string PeerId { get; set; } = "";
    public string? Candidate { get; set; }
    public string? SdpMid { get; set; }
    public int SdpMLineIndex { get; set; }
}

/// <summary>
/// Direct-RTP session answer (M3, /signaling type "rtp"). Android needs
/// exactly three facts: our SDES crypto line (unprotect inbound), the UDP
/// port our session bound (informational), and the stream SSRC.
/// </summary>
public record RtpSessionAnswer(
    string Type,
    string Crypto,
    uint Ssrc,
    int RtpPort);
