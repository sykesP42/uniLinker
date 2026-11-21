using System.Net;
using System.Text;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

/// <summary>
/// Shared RTP timestamp logic for both media send paths: WebRTC
/// (PeerChannel) and direct UDP (DirectRtpChannel). SIPSorcery's SendVideo
/// takes a DURATION in RTP units (added to the previous timestamp), not an
/// absolute timestamp — deriving it from the real capture timestamps keeps
/// the 90 kHz clock tracking the actual frame cadence.
/// </summary>
/// <summary>
/// Direct-RTP media channel (M3, docs/spec/M3-direct-rtp.md): fixed-endpoint
/// UDP RTP with SDES-keyed SRTP — NO ICE, NO DTLS, NO SDP round trip with the
/// receiver. Implements IChannel so ScreenMirrorPlugin treats it as a normal
/// viewer (IDR on join, same send path).
///
/// Transport: SIPSorcery RTPSession in "secure + SDP crypto" mode. Our offer
/// announces OUR master key (a=crypto line, delivered to Android over the
/// HTTP control plane); the answer is fabricated locally (peer-side key is
/// random filler — M3 is one-way, only OUR key is on the protection path).
/// The Android side unprotects with the announced key via jitsi-srtp.
/// </summary>
internal class DirectRtpChannelLegacy : IDirectRtpChannel
{
    private readonly IPEndPoint _destination;
    private readonly int _bindPort;
    private RTPSession? _session;

    private long _lastFrameTsUs = -1;
    private uint _lastDurationRtp = RtpTiming.DefaultDurationRtp;

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public ChannelType Type => ChannelType.MediaTrack;
    public PeerInfo RemotePeer { get; }
    public string Capability { get; }
    public ChannelState State { get; private set; } = ChannelState.Connecting;
    public bool IsOpen => State == ChannelState.Open;
#pragma warning disable CS0067
    public event Action? OnClose;
    public event Action<byte[]>? MessageReceived;
    public event Action<EncodedPacket>? PacketReceived;
#pragma warning restore CS0067

    /// <summary>The stream's SSRC, taken from the generated offer (0 if the
    /// offer carries none — the receiver keys per-SSRC on first packet).</summary>
    public uint Ssrc { get; private set; }

    /// <summary>The legacy session pipeline has no tile queue — no pressure signal.</summary>
    public int TileBacklog => 0;

    /// <summary>
    /// The SDES crypto attribute from OUR generated offer — the key SIPSorcery
    /// actually protects outbound packets with (interop-diagnosed: it is NOT
    /// an externally injected key). Android unprotects with this exact line.
    /// Valid only after a successful OpenAsync.
    /// </summary>
    public string CryptoLine { get; private set; } = "";

    public DirectRtpChannelLegacy(
        PeerInfo peer,
        string capability,
        IPEndPoint destination,
        int bindPort = DirectRtpChannel.DefaultRtpPort,
        int requestedWidth = 0,
        int requestedHeight = 0,
        int requestedBitrateKbps = 0,
        int requestedFps = 0)
    {
        RemotePeer = peer;
        Capability = capability;
        _destination = destination;
        _bindPort = bindPort;
        RequestedWidth = requestedWidth;
        RequestedHeight = requestedHeight;
        RequestedBitrateKbps = requestedBitrateKbps;
        RequestedFps = requestedFps;
    }

    /// <inheritdoc />
    public int RequestedWidth { get; }
    /// <inheritdoc />
    public int RequestedHeight { get; }
    /// <inheritdoc />
    public int RequestedBitrateKbps { get; }
    /// <inheritdoc />
    public int RequestedFps { get; }

    /// <summary>Bring the session up: bind, generate offer, fabricate the
    /// SDES answer, set destination. Throws when the SIPSorcery SDP state
    /// machine rejects the setup (transport maps this to 500).</summary>
    public async Task OpenAsync()
    {
        var config = new RtpSessionConfig
        {
            IsMediaMultiplexed = false,
            IsRtcpMultiplexed = true,
            RtpSecureMediaOption = RtpSecureMediaOptionEnum.SdpCryptoNegotiation,
            BindPort = _bindPort,
        };
        var session = new RTPSession(config);
        try
        {
            var videoFormat = new VideoFormat(VideoCodecsEnum.H264, 96, RtpTiming.ClockRate,
                "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f");
            session.addTrack(new MediaStreamTrack(videoFormat, MediaStreamStatusEnum.SendOnly));

            var offer = session.CreateOffer(_destination.Address);
            Ssrc = ExtractSsrc(offer.ToString() ?? "");
            CryptoLine = ExtractCryptoLine(offer.ToString() ?? "");
            if (CryptoLine.Length == 0)
            {
                throw new InvalidOperationException("offer carries no a=crypto line (SDES not negotiated)");
            }

            var answerSdp = FabricateAnswer(offer.ToString() ?? "", _destination);
            var result = session.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(answerSdp));
            if (result != SetDescriptionResultEnum.OK)
            {
                throw new InvalidOperationException($"Direct RTP SDES setup failed: {result}");
            }

            session.SetDestination(SDPMediaTypesEnum.video, _destination, _destination);
            await session.Start();

            _session = session;
            State = ChannelState.Open;
            Log.Info($"Direct RTP channel {Id} open: dest={_destination} bind={_bindPort} ssrc={Ssrc}");
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public Task SendAsync(byte[] data) =>
        throw new NotSupportedException("MediaTrack channels use SendPacketAsync");

    public async Task SendPacketAsync(EncodedPacket packet)
    {
        var session = _session;
        if (session == null || State != ChannelState.Open) return;

        uint durationRtp = RtpTiming.DurationFor(packet.TimestampUs, ref _lastFrameTsUs, ref _lastDurationRtp);
        session.SendVideo(durationRtp, packet.Data);
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        State = ChannelState.Closed;
        try { _session?.Close("channel disposed"); } catch { }
        try { _session?.Dispose(); } catch { }
        _session = null;
        try
        {
            OnClose?.Invoke();
        }
        catch (Exception ex)
        {
            // A viewer-release handler must never break disposal (reconnect
            // flow: the stale channel is disposed before the new one binds).
            Log.Info($"Direct RTP channel {Id} OnClose handler threw: {ex.Message}");
        }
    }

    private static uint ExtractSsrc(string offerSdp)
    {
        foreach (var line in offerSdp.Split('\n'))
        {
            var l = line.Trim();
            if (l.StartsWith("a=ssrc:", StringComparison.Ordinal))
            {
                var value = l["a=ssrc:".Length..];
                var ssrc = value.Split(' ', ':')[0];
                if (uint.TryParse(ssrc, out var parsed)) return parsed;
            }
        }
        return 0;
    }

    /// <summary>Pull the "a=crypto:" line out of our own offer — that key is
    /// what SIPSorcery's SRTP handler actually protects outbound with.</summary>
    private static string ExtractCryptoLine(string offerSdp)
    {
        foreach (var line in offerSdp.Replace("\r\n", "\n").Split('\n'))
        {
            var l = line.Trim();
            if (l.StartsWith("a=crypto:", StringComparison.Ordinal))
            {
                return l["a=crypto:".Length..].Trim();
            }
        }
        return "";
    }

    /// <summary>
    /// Fabricate the remote (Android-side) answer from our offer by text
    /// transformation: point the media at the Android listener, flip the
    /// direction to recvonly, strip ICE (plain RTPSession has none), and add
    /// the crypto line so SIPSorcery's SDES state machine completes.
    ///
    /// The answer ECHOES OUR OWN key. Empirically (interop diagnosis test)
    /// SIPSorcery's outbound SRTP handler derives its protection key from the
    /// remote description, not the local one — echoing our key makes the
    /// emitted packets decryptable with the crypto line we hand to Android
    /// regardless of which side it picks. M3 is one-way, so key reuse across
    /// the two descriptions is harmless; revisit when the reverse direction
    /// ships.
    /// </summary>
    private string FabricateAnswer(string offerSdp, IPEndPoint destination)
    {
        // Echo our own crypto line (see member doc on the key-direction quirk).
        var peerKey = CryptoLine;
        var sb = new StringBuilder();
        var inMedia = false;
        var mediaRewritten = false;

        foreach (var raw in offerSdp.Replace("\r\n", "\n").Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0) continue;

            if (l.StartsWith("m=", StringComparison.Ordinal))
            {
                inMedia = true;
                // m=video <port> <proto> <fmts> → destination port, same proto/formats
                var parts = l.Split(' ');
                if (parts.Length >= 4 && parts[0] == "m=video")
                {
                    sb.Append($"m=video {destination.Port} {parts[2]} {string.Join(' ', parts[3..])}\r\n");
                    sb.Append($"c=IN IP4 {destination.Address}\r\n");
                    mediaRewritten = true;
                }
                else
                {
                    sb.Append(l).Append("\r\n");
                }
                continue;
            }

            // Strip anything ICE-flavored from the offer.
            if (l.StartsWith("a=candidate", StringComparison.Ordinal) ||
                l.StartsWith("a=ice-", StringComparison.Ordinal) ||
                l.StartsWith("a=end-of-candidates", StringComparison.Ordinal) ||
                l.StartsWith("a=setup", StringComparison.Ordinal))
            {
                continue;
            }

            if (inMedia && l.StartsWith("a=", StringComparison.Ordinal) &&
                (l.StartsWith("a=sendrecv", StringComparison.Ordinal) ||
                 l.StartsWith("a=sendonly", StringComparison.Ordinal) ||
                 l.StartsWith("a=recvonly", StringComparison.Ordinal) ||
                 l.StartsWith("a=inactive", StringComparison.Ordinal)))
            {
                sb.Append("a=recvonly\r\n");
                continue;
            }

            sb.Append(l).Append("\r\n");

            if (mediaRewritten && l.StartsWith("a=fmtp:", StringComparison.Ordinal))
            {
                // Peer crypto goes right after the format declaration.
                sb.Append($"a=crypto:{peerKey}\r\n");
            }
        }

        if (!sb.ToString().Contains("a=crypto:", StringComparison.Ordinal))
        {
            sb.Append($"a=crypto:{peerKey}\r\n");
        }
        return sb.ToString();
    }
}
