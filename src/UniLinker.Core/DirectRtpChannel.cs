using System.Net;
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
/// Transport (M5): own-pipeline <see cref="DirectRtpSender"/> — RFC 6184
/// packetization, SrtpTransformEngine protection (probed P2: reusable
/// without DTLS with own keys), and an RTX ring buffer answering receiver
/// NACKs. The Android side unprotects with the announced crypto line and
/// NACKs gaps back to the same port.
/// </summary>
internal class DirectRtpChannel : IDirectRtpChannel
{
    public const int DefaultRtpPort = 5004;
    public const string CryptoSuite = "AES_CM_128_HMAC_SHA1_80";

    private readonly IPEndPoint _destination;
    private readonly int _bindPort;
    private DirectRtpSender? _sender;
    private readonly bool _tileMode = DirectRtpTransport.Mode == "tile";

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

    /// <summary>
    /// The SDES crypto attribute from OUR generated offer — the key SIPSorcery
    /// actually protects outbound packets with (interop-diagnosed: it is NOT
    /// an externally injected key). Android unprotects with this exact line.
    /// Valid only after a successful OpenAsync.
    /// </summary>
    public string CryptoLine { get; private set; } = "";

    public DirectRtpChannel(
        PeerInfo peer,
        string capability,
        IPEndPoint destination,
        int bindPort = DefaultRtpPort,
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

    /// <summary>Bring the session up: bind the sender socket, generate our
    /// key material + crypto line (the receiver unprotects with it), start
    /// the NACK loop. Throws when the bind port is taken (transport → 500).
    /// Replaces the M3 RTPSession SDES dance: the crypto line format is
    /// identical, so the Android wire contract is unchanged.</summary>
    public Task OpenAsync()
    {
        var sender = new DirectRtpSender(
            _destination, _bindPort,
            RequestedBitrateKbps > 0 ? RequestedBitrateKbps : 8000,
            tileMode: DirectRtpTransport.Mode == "tile");
        try
        {
            sender.Start();
            _sender = sender;
            Ssrc = sender.Ssrc;
            CryptoLine = sender.CryptoLine;
            State = ChannelState.Open;
            Log.Info($"Direct RTP channel {Id} open: dest={_destination} bind={_bindPort} ssrc={Ssrc} (own-pipeline RTX)");
            return Task.CompletedTask;
        }
        catch
        {
            sender.Dispose();
            throw;
        }
    }

    public Task SendAsync(byte[] data) =>
        throw new NotSupportedException("MediaTrack channels use SendPacketAsync");

    public Task SendPacketAsync(EncodedPacket packet)
    {
        if (_tileMode)
        {
            // Tile wire payload: [...jpeg][flags u8], flags bit0 = last
            // packet of the tile frame (the RTP marker source).
            _sender?.SendTileAu(packet.Data, packet.TimestampUs,
                isLast: (packet.Data[^1] & 1) != 0,
                isKeyFrame: packet.IsKeyFrame);
            return Task.CompletedTask;
        }
        _sender?.SendAu(packet.Data, packet.TimestampUs, packet.IsKeyFrame);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        State = ChannelState.Closed;
        try { _sender?.Dispose(); } catch { }
        _sender = null;
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

}