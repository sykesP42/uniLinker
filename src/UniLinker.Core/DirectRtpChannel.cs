using System.Net;
using System.Security.Cryptography;
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
internal static class RtpTiming
{
    public const int ClockRate = 90000;
    public const int DefaultDurationRtp = ClockRate / 30;

    /// <summary>Derive the RTP duration for this frame from its capture
    /// timestamp. Clamped to 1..120 fps so a bad timestamp cannot emit an
    /// absurd gap; a non-advancing timestamp (idle re-emit of a cached frame)
    /// repeats the previous duration instead of freezing the playout clock.</summary>
    public static uint DurationFor(long timestampUs, ref long lastFrameTsUs, ref uint lastDurationRtp)
    {
        uint durationRtp;
        if (lastFrameTsUs < 0)
        {
            durationRtp = lastDurationRtp; // first frame: assume nominal 30 fps
        }
        else
        {
            long deltaUs = timestampUs - lastFrameTsUs;
            if (deltaUs <= 0)
            {
                durationRtp = lastDurationRtp;
            }
            else
            {
                // microseconds -> 90 kHz ticks: us * 90000 / 1_000_000 == us * 9 / 100
                long ticks = deltaUs * 9 / 100;
                durationRtp = (uint)Math.Clamp(ticks, 750, ClockRate);
            }
        }
        lastFrameTsUs = timestampUs;
        lastDurationRtp = durationRtp;
        return durationRtp;
    }
}

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
internal class DirectRtpChannel : IChannel
{
    public const int DefaultRtpPort = 5004;
    public const string CryptoSuite = "AES_CM_128_HMAC_SHA1_80";

    private readonly IPEndPoint _destination;
    private readonly int _bindPort;
    private readonly byte[] _masterKey;
    private readonly byte[] _masterSalt;
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

    /// <summary>The SDES crypto attribute Android unprotects our stream with
    /// (RFC 4568: "1 &lt;suite&gt; inline:&lt;base64(16B key || 14B salt)&gt;").</summary>
    public string CryptoLine => $"1 {CryptoSuite} inline:{Convert.ToBase64String(_masterKey.Concat(_masterSalt).ToArray())}";

    public DirectRtpChannel(
        PeerInfo peer,
        string capability,
        IPEndPoint destination,
        int bindPort = DefaultRtpPort,
        byte[]? masterKey = null,
        byte[]? masterSalt = null)
    {
        RemotePeer = peer;
        Capability = capability;
        _destination = destination;
        _bindPort = bindPort;
        _masterKey = masterKey ?? RandomNumberGenerator.GetBytes(16);
        _masterSalt = masterSalt ?? RandomNumberGenerator.GetBytes(14);
    }

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
        OnClose?.Invoke();
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

    /// <summary>
    /// Fabricate the remote (Android-side) answer from our offer by text
    /// transformation: point the media at the Android listener, flip the
    /// direction to recvonly, strip ICE (plain RTPSession has none), and add
    /// the peer-side crypto line so SIPSorcery's SDES state machine completes.
    /// </summary>
    private static string FabricateAnswer(string offerSdp, IPEndPoint destination)
    {
        var peerKey = $"inline:{Convert.ToBase64String(RandomNumberGenerator.GetBytes(16).Concat(RandomNumberGenerator.GetBytes(14)).ToArray())}";
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
                sb.Append($"a=crypto:1 {CryptoSuite} {peerKey}\r\n");
            }
        }

        if (!sb.ToString().Contains("a=crypto:", StringComparison.Ordinal))
        {
            sb.Append($"a=crypto:1 {CryptoSuite} {peerKey}\r\n");
        }
        return sb.ToString();
    }
}
