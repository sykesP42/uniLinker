using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using SIPSorcery.Net;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Core;

/// <summary>
/// Own-pipeline direct-RTP sender (M5): replaces the SIPSorcery RTPSession
/// media path so the sender can serve RTX retransmissions. SIPSorcery's
/// session owns its RTP packets after SendVideo — no ring buffer, no NACK
/// hook. Here we packetize the H.264 Annex-B access unit ourselves
/// (RFC 6184 single-NAL / FU-A), protect with the probed-usable
/// <see cref="SrtpTransformEngine"/> (no DTLS, own keys), keep the last
/// <see cref="RingCapacity"/> protected packets keyed by sequence number,
/// and resend on receiver NACKs (0 ms RTT on the tethered link — the
/// original protected bytes stay inside the receiver's SRTP replay window).
///
/// Packet pacer: AUs arrive on the ENCODE worker; a dedicated thread sends
/// them through a token bucket so a 300KB IDR trickles over ~40ms instead
/// of bursting 200 packets into the receiver's UDP buffer.
///
/// Wire compatibility: the receiver sees the same crypto line format
/// ("1 AES_CM_128_HMAC_SHA1_80 inline:base64(key16+salt14)"), the same
/// PT 96 + FU-A/marker framing, and the same answer JSON fields.
/// </summary>
internal sealed class DirectRtpSender : IDisposable
{
    /// <summary>RTP payload budget per packet: 1500 MTU − 20 IP − 8 UDP
    /// − 12 RTP − 10 SRTP tag, trimmed for tunnel overhead.</summary>
    public const int MaxPayload = 1400;
    public const int RingCapacity = 4096;
    public const uint NackMagic = 0x554E494E; // "UNIN"

    private readonly IPEndPoint _destination;
    private readonly UdpClient _udp;
    private readonly SrtpTransformEngine _engine;
    private readonly SrtpTransformer _transformer;
    private readonly CancellationTokenSource _nackLoopCts = new();
    private Task? _nackLoop;
    private bool _disposed;

    private readonly (ushort Seq, byte[] Packet, long LastResendMs)[] _ring = new (ushort, byte[], long)[RingCapacity];
    private int _ringCursor;
    /// <summary>Per-seq resend dedup window: duplicate NACK requests inside
    /// the window skip the resend (the tablet throttles itself to 15ms per
    /// hole; 10ms here lets a legitimate retry through but collapses a
    /// request storm to ≤100 resends per seq per second).</summary>
    private const long ResendDedupMs = 10;
    private readonly object _sendGate = new();

    private ushort _seq;
    private uint _timestamp;
    private long _lastFrameTsUs = -1;
    private uint _lastDurationRtp = RtpTiming.DefaultDurationRtp;

    // ── Packet pacer (M5) ──
    // SendAu runs on the ENCODE worker; pacing inline would stall it. AUs
    // queue here and a dedicated thread sends with a token bucket so a
    // 300KB IDR trickles over ~40ms instead of bursting 200 packets into
    // the receiver's UDP buffer. Bucket drains at 1.5× the tier bitrate —
    // faster than real-time so the queue stays empty in steady state.
    private readonly System.Collections.Concurrent.BlockingCollection<(byte[] Au, long TsUs)> _pacerQueue =
        new(6);
    private readonly System.Collections.Concurrent.BlockingCollection<(byte[] Blob, long TsUs, bool IsKey)> _tileQueue =
        new(4);
    private Thread? _pacerThread;
    private readonly CancellationTokenSource _pacerCts = new();
    private readonly long _pacerBytesPerMs;
    private readonly bool _tileMode;
    private long _lastTileTsUs = -1;
    /// <summary>Instant-burst allowance: the first ~42 packets of a frame go
    /// out at once, the rest of a large IDR trickles at 1.5× tier.</summary>
    // 16KB (11 packets): the instant burst rides on top of the paced drain,
    // and 64KB spikes overflowed the WiFi hotspot queue as burst-TAIL drops
    // — a systematic 1-8 packet hole every frame (USB buffers absorb 64KB
    // fine; the hotspot queue does not).
    private const long MaxInstantBurstBytes = 16 * 1024;

    /// <summary>The SDES crypto attribute the receiver unprotects with.</summary>
    public string CryptoLine { get; }
    /// <summary>The stream SSRC.</summary>
    public uint Ssrc { get; }
    /// <summary>Local bound port (the answer's rtpPort — also the NACK target).</summary>
    public int BindPort { get; }
    /// <summary>Packets sent (diagnostics).</summary>
    public long PacketsSent { get; private set; }
    /// <summary>Packets retransmitted on NACK (diagnostics).</summary>
    public long PacketsRetransmitted { get; private set; }
    /// <summary>NACK datagrams received (diagnostics).</summary>
    public long NacksReceived { get; private set; }

    public DirectRtpSender(IPEndPoint destination, int bindPort, int bitrateKbps = 8000, bool tileMode = false)
    {
        _destination = destination;
        BindPort = bindPort;
        _tileMode = tileMode;
        // ~1.1× the tier bitrate, in bytes/ms: the tier IS the link budget
        // (vbr+cq caps at maxrate), and the WiFi hotspot link carries only
        // ~its rated capacity — the earlier 2.5× burst rate overflowed the
        // hotspot airtime and caused real WiFi packet loss (12-34%).
        // 32000kbps → 4000 B/ms tier → ×1.1 = 4400 B/ms.
        _pacerBytesPerMs = (long)bitrateKbps * 11 / 80; // 32000kbps -> 4400 B/ms = 4.4MB/s

        byte[] masterKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        byte[] masterSalt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(14);
        var srtpPolicy = new SrtpPolicy(
            SrtpPolicy.AESCM_ENCRYPTION, 16,
            SrtpPolicy.HMACSHA1_AUTHENTICATION, 20,
            authTagLength: 10, saltKeyLength: 14);
        var srtcpPolicy = new SrtpPolicy(
            SrtpPolicy.AESCM_ENCRYPTION, 16,
            SrtpPolicy.HMACSHA1_AUTHENTICATION, 20,
            authTagLength: 10, saltKeyLength: 14);
        _engine = new SrtpTransformEngine(masterKey, masterSalt, srtpPolicy, srtcpPolicy);
        _transformer = new SrtpTransformer(_engine);

        Ssrc = BitConverter.ToUInt32(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(4));
        CryptoLine = $"1 {DirectRtpChannel.CryptoSuite} inline:" +
                     Convert.ToBase64String(masterKey.Concat(masterSalt).ToArray());

        // IPv4 bound (receiver NACKs and Android targets are IPv4 on the
        // tethered link). 1 MB receive buffer for the bursty uplink.
        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, bindPort));
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 1 << 20);
        // Windows-only: a send to a closed remote port triggers ICMP, which
        // makes the NEXT receive throw WSAECONNRESET and kills the NACK loop
        // silently (empirically killed it on the first viewer churn). Disable.
        try
        {
            const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
            _udp.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
        }
        catch { } // non-Windows

        _pacerThread = new Thread(PacerLoop) { IsBackground = true, Name = "RtpPacer" };
        _pacerThread.Start();
    }

    /// <summary>Start the NACK receive loop.</summary>
    public void Start()
    {
        _nackLoop = Task.Run(() => NackLoopAsync(_nackLoopCts.Token));
    }

    private async Task NackLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(ct);
                HandleDatagram(result.Buffer);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }       // transient (ICMP reset, timeout)
            catch (Exception ex)
            {
                Log.Info($"[RTX] nack loop error: {ex.Message}");
            }
        }
    }

    /// <summary>Parse a NACK datagram and resend the requested sequences.
    /// Format: magic "UNIN" (4B BE) + count (2B BE) + count × seq (2B BE).</summary>
    internal void HandleDatagram(byte[] datagram)
    {
        if (_disposed || datagram.Length < 6) return;
        if (BinaryPrimitives.ReadUInt32BigEndian(datagram) != NackMagic) return;
        int count = BinaryPrimitives.ReadUInt16BigEndian(datagram.AsSpan(4));
        if (datagram.Length < 6 + count * 2) return;
        NacksReceived++;
        if (NacksReceived % 20 == 1)
        {
            Log.Info($"[RTX] nack #{NacksReceived} ({count} seqs, sent={PacketsSent} resent={PacketsRetransmitted})");
        }
        lock (_sendGate)
        {
            var misses = new List<ushort>();
            long nowMs = Environment.TickCount64;
            for (int i = 0; i < count; i++)
            {
                ushort seq = BinaryPrimitives.ReadUInt16BigEndian(datagram.AsSpan(6 + i * 2));
                // Scan the ring for the seq — NACKs are rare and the ring is
                // 4096 entries; a cursor-keyed index buys nothing here.
                bool found = false;
                for (int slot = 0; slot < _ring.Length; slot++)
                {
                    var (s, packet, lastResendMs) = _ring[slot];
                    if (packet != null && s == seq)
                    {
                        if (nowMs - lastResendMs >= ResendDedupMs)
                        {
                            _udp.Send(packet, packet.Length, _destination);
                            PacketsRetransmitted++;
                            _ring[slot] = (s, packet, nowMs);
                        }
                        found = true;
                        break;
                    }
                }
                if (!found) misses.Add(seq);
            }
            if (misses.Count > 0 && NacksReceived % 20 == 1)
            {
                Log.Info($"[RTX] {misses.Count}/{count} seqs missing from ring (first={misses[0]}, ringCursor={_ringCursor})");
            }
        }
    }

    /// <summary>Enqueue one access unit for the paced sender. Bounded: when
    /// full, drop the oldest AU (stale P frames behind a burst) — a
    /// keyframe clears the queue outright so recovery frames never wait
    /// behind stale traffic.</summary>
    public void SendAu(byte[] annexBAu, long timestampUs, bool isKeyFrame = false)
    {
        if (_disposed) return;
        if (isKeyFrame)
        {
            while (_pacerQueue.Count > 0) _pacerQueue.TryTake(out _);
        }
        else
        {
            while (_pacerQueue.Count >= 6) _pacerQueue.TryTake(out _);
        }
        try
        {
            _pacerQueue.Add((annexBAu, timestampUs), _pacerCts.Token);
        }
        catch (InvalidOperationException) { } // completing on dispose
    }

    /// <summary>Tile mode: enqueue one frame blob (all wire packets
    /// concatenated, u16-length-prefixed). Queue cap 4 FRAMES — dropping a
    /// whole stale frame is safe (latest-frame-wins rendering); tearing a
    /// full refresh into queue-overflow holes painted black tiles.</summary>
    public void SendTileAu(byte[] blob, long timestampUs, bool isKeyFrame)
    {
        if (_disposed) return;
        if (isKeyFrame)
        {
            while (_tileQueue.Count > 0) _tileQueue.TryTake(out _);
        }
        else
        {
            while (_tileQueue.Count >= 4) _tileQueue.TryTake(out _);
        }
        try
        {
            _tileQueue.Add((blob, timestampUs, isKeyFrame), _pacerCts.Token);
        }
        catch (InvalidOperationException) { }
    }

    /// <summary>Paced sender: packetize + protect + token-bucket send.
    /// Serialized under the send gate — the SRTP engine is not thread-safe;
    /// NACK resends take the same gate.</summary>
    private void PacerLoop()
    {
        // Continuous-refill token bucket: credit accumulates at 1.1× tier
        // bitrate (the tier IS the link budget), capped at 64KB instant burst. The earlier "spread from
        // burst start + carry debt" formulation re-applied the full carried
        // bytes against a FRESH burst start each AU — a 300ms penalty per
        // frame that throttled the stream to 2fps.
        long creditBytes = MaxInstantBurstBytes;
        var lastRefill = System.Diagnostics.Stopwatch.GetTimestamp();

        void Refill()
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var elapsedMs = (now - lastRefill) * 1000 / System.Diagnostics.Stopwatch.Frequency;
            if (elapsedMs > 0)
            {
                creditBytes = Math.Min(creditBytes + elapsedMs * _pacerBytesPerMs,
                    MaxInstantBurstBytes);
                lastRefill = now;
            }
        }

        while (!_pacerCts.IsCancellationRequested)
        {
            (byte[] Au, long TsUs, bool? IsLast) item;
            try
            {
                if (_tileMode)
                {
                    var (blob, ts, key) = _tileQueue.Take(_pacerCts.Token);
                    item = (blob, ts, key);
                }
                else
                {
                    var (au, ts) = _pacerQueue.Take(_pacerCts.Token);
                    item = (au, ts, null);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (InvalidOperationException) { break; }

            // Per-packet send: credit is waited for OUTSIDE the send gate —
            // the gate is taken only for the (fast) transform+ring+send. The
            // earlier whole-frame lock held the gate across the credit sleep,
            // which starved NACK resends (blocked up to a frame's drain time
            // behind a throttling media send) and stalled hole recovery.
            try
            {
                if (_tileMode)
                {
                    // Tile frames share one RTP timestamp (the frame id);
                    // advance the 90 kHz clock once per frame.
                    if (item.TsUs != _lastTileTsUs)
                    {
                        uint durationRtp = RtpTiming.DurationFor(item.TsUs, ref _lastFrameTsUs, ref _lastDurationRtp);
                        _timestamp += durationRtp;
                        _lastTileTsUs = item.TsUs;
                    }
                    // Split the frame blob into its wire packets and pace
                    // each; the tile assembler keys on the timestamp.
                    var pos = 0;
                    var blobData = item.Au;
                    while (pos + 2 <= blobData.Length)
                    {
                        int len = (blobData[pos] << 8) | blobData[pos + 1];
                        var rtp = new byte[12 + len];
                        rtp[0] = 0x80;
                        rtp[1] = 97;
                        FillRtpHeader(rtp, _timestamp);
                        Buffer.BlockCopy(blobData, pos + 2, rtp, 12, len);

                        // Wait for credit BEFORE taking the gate. Without a
                        // credit wait at all the frame blasted instantly and
                        // credit went unboundedly negative (the tile wire
                        // rate ignored the tier budget entirely; ~27Mbps
                        // measured on an 8.8Mbps budget, which overflowed
                        // the WiFi airtime and poisoned every recovery).
                        while (!_pacerCts.IsCancellationRequested)
                        {
                            Refill();
                            if (creditBytes >= rtp.Length) break;
                            Thread.Sleep(2);
                        }
                        if (_pacerCts.IsCancellationRequested) break;
                        creditBytes -= rtp.Length;

                        lock (_sendGate)
                        {
                            if (_disposed) break;
                            byte[]? protectedPacket = _transformer.Transform(rtp);
                            if (protectedPacket != null)
                            {
                                _ringCursor = (_ringCursor + 1) % RingCapacity;
                                _ring[_ringCursor] = ((ushort)((rtp[2] << 8) | rtp[3]), protectedPacket, 0);
                                _udp.Send(protectedPacket, protectedPacket.Length, _destination);
                                PacketsSent++;
                            }
                        }
                        pos += 2 + len;
                    }
                }
                else
                {
                    uint durationRtp = RtpTiming.DurationFor(item.TsUs, ref _lastFrameTsUs, ref _lastDurationRtp);
                    _timestamp += durationRtp;
                    var packets = PacketizeAu(item.Au, _timestamp);
                    foreach (var rtp in packets)
                    {
                        // Wait for credit before committing the packet.
                        while (!_pacerCts.IsCancellationRequested)
                        {
                            Refill();
                            if (creditBytes >= rtp.Length) break;
                            Thread.Sleep(2);
                        }
                        if (_pacerCts.IsCancellationRequested) break;
                        creditBytes -= rtp.Length;

                        ushort seq = (ushort)((rtp[2] << 8) | rtp[3]);
                        lock (_sendGate)
                        {
                            if (_disposed) break;
                            byte[]? protectedPacket = _transformer.Transform(rtp);
                            if (protectedPacket == null)
                            {
                                Log.Info("[RTX] srtp transform returned null — packet dropped");
                                continue;
                            }
                            _ringCursor = (_ringCursor + 1) % RingCapacity;
                            _ring[_ringCursor] = (seq, protectedPacket, 0);
                            _udp.Send(protectedPacket, protectedPacket.Length, _destination);
                            PacketsSent++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[pacer] send failed: {ex.Message}");
            }
        }
    }

    /// <summary>Split the Annex-B access unit into RTP packets: single-NAL
    /// packets when the NAL fits, FU-A fragmentation otherwise. Marker bit is
    /// set on the last packet of the AU (the receiver's frame boundary).
    /// Sequence numbers are assigned here in send order.</summary>
    internal List<byte[]> PacketizeAu(byte[] au, uint timestamp)
    {
        var result = new List<byte[]>();
        var nals = SplitNals(au);

        foreach (var nal in nals)
        {
            bool lastNal = ReferenceEquals(nal, nals[^1]);
            if (nal.Length <= MaxPayload)
            {
                result.Add(BuildSingleNal(nal, timestamp, marker: lastNal));
            }
            else
            {
                // FU-A: indicator byte (F=0, NRI from NAL[0], type=28) +
                // FU header (S/E bits, type from NAL[0]) + chunks.
                byte nalHeader = nal[0];
                byte indicator = (byte)((nalHeader & 0x60) | 28);
                byte nalType = (byte)(nalHeader & 0x1F);
                var payload = nal.AsSpan(1);
                int offset = 0;
                int chunkMax = MaxPayload - 2;
                while (offset < payload.Length)
                {
                    int chunkLen = Math.Min(chunkMax, payload.Length - offset);
                    bool first = offset == 0;
                    bool last = offset + chunkLen == payload.Length;
                    byte fuHeader = (byte)(nalType | (first ? 0x80 : 0) | (last ? 0x40 : 0));

                    var rtp = new byte[12 + 2 + chunkLen];
                    rtp[0] = 0x80;
                    rtp[1] = (byte)(96 | (last && lastNal ? 0x80 : 0));
                    FillRtpHeader(rtp, timestamp);
                    rtp[12] = indicator;
                    rtp[13] = fuHeader;
                    payload.Slice(offset, chunkLen).CopyTo(rtp.AsSpan(14));
                    result.Add(rtp);
                    offset += chunkLen;
                }
            }
        }
        return result;
    }

    private void FillRtpHeader(byte[] rtp, uint timestamp)
    {
        ushort seq = _seq++;
        rtp[2] = (byte)(seq >> 8);
        rtp[3] = (byte)seq;
        rtp[4] = (byte)(timestamp >> 24);
        rtp[5] = (byte)(timestamp >> 16);
        rtp[6] = (byte)(timestamp >> 8);
        rtp[7] = (byte)timestamp;
        rtp[8] = (byte)(Ssrc >> 24);
        rtp[9] = (byte)(Ssrc >> 16);
        rtp[10] = (byte)(Ssrc >> 8);
        rtp[11] = (byte)Ssrc;
    }

    private byte[] BuildSingleNal(byte[] nalPayload, uint timestamp, bool marker)
    {
        var packet = new byte[12 + nalPayload.Length];
        packet[0] = 0x80;
        packet[1] = (byte)(96 | (marker ? 0x80 : 0));
        FillRtpHeader(packet, timestamp);
        Buffer.BlockCopy(nalPayload, 0, packet, 12, nalPayload.Length);
        return packet;
    }

    /// <summary>Split an Annex-B stream into NAL byte ranges (start codes
    /// stripped). Handles 3- and 4-byte start codes; a buffer with no start
    /// code at all is treated as one raw NAL (test vectors, raw payload).</summary>
    internal static List<byte[]> SplitNals(byte[] au)
    {
        var nals = new List<byte[]>();
        int i = 0;
        int nalStart = -1;
        bool sawStartCode = false;
        while (i < au.Length - 3)
        {
            bool sc3 = au[i] == 0 && au[i + 1] == 0 && au[i + 2] == 1;
            bool sc4 = au[i] == 0 && au[i + 1] == 0 && au[i + 2] == 0 && au[i + 3] == 1;
            if (sc3 || sc4)
            {
                sawStartCode = true;
                int scLen = sc4 ? 4 : 3;
                if (nalStart >= 0)
                {
                    nals.Add(au[nalStart..i]);
                }
                nalStart = i + scLen;
                i += scLen;
            }
            else
            {
                i++;
            }
        }
        if (nalStart >= 0 && nalStart < au.Length)
        {
            nals.Add(au[nalStart..au.Length]);
        }
        if (!sawStartCode && au.Length > 0)
        {
            nals.Add(au);
        }
        return nals;
    }

    public void Dispose()
    {
        // Take the send gate FIRST: an in-flight SendAu -> Transform on the
        // native SRTP engine raced Dispose's Close and crashed coreclr
        // (0xc0000005). Holding the gate waits out the in-flight encode,
        // and the disposed flag makes any queued SendAu bail.
        lock (_sendGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _pacerCts.Cancel();
        try { _pacerThread?.Join(1000); } catch { }
        _nackLoopCts.Cancel();
        try { _udp.Close(); } catch { }
        try { _nackLoop?.Wait(1000); } catch { }
        try { _transformer.Close(); } catch { }
        try { _engine.Close(); } catch { }
    }
}
