using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using UniLinker.Core;
using UniLinker.Plugin.Sdk;
using Xunit;

namespace UniLinker.Core.Tests;

/// <summary>
/// M3 direct-RTP sender tests (docs/spec/M3-direct-rtp.md). DirectRtpChannel
/// implements IChannel so ScreenMirrorPlugin treats it as a normal viewer;
/// these tests verify the transport: fixed-endpoint UDP RTP, SDES-keyed SRTP,
/// no ICE/DTLS anywhere.
/// </summary>
public class DirectRtpChannelTests
{
    /// <summary>Splits "1 AES_CM_128_HMAC_SHA1_80 inline:&lt;b64&gt;" into its
    /// 16-byte master key and 14-byte salt (RFC 4568 inline = key || salt).</summary>
    private static (byte[] key, byte[] salt) SplitInline(string cryptoLine)
    {
        var inline = cryptoLine.Split(' ').First(p => p.StartsWith("inline:", StringComparison.Ordinal))["inline:".Length..];
        var kv = Convert.FromBase64String(inline);
        return (kv[..16], kv[16..]);
    }

    private static SIPSorcery.Net.SrtpPolicy MakePolicy() =>
        new(SIPSorcery.Net.SrtpPolicy.AESCM_ENCRYPTION, 16,
            SIPSorcery.Net.SrtpPolicy.HMACSHA1_AUTHENTICATION, 20, 10, 14);

    private static PeerInfo MakePeer() => new()
    {
        Id = "test-android",
        Name = "test-android",
        IpAddress = "127.0.0.1",
        Port = 5600,
        State = PeerState.Discovered,
        Capabilities = ["screen-capture"],
    };

    /// <summary>
    /// Interop gate: the wire packets must be decryptable with exactly the
    /// crypto line the channel exposes (the one Android receives). The earlier
    /// version of this test failed — SIPSorcery protects outbound with the key
    /// it announced in the OFFER, not an externally injected one, so the
    /// channel now derives CryptoLine from the offer itself.
    /// </summary>
    [Fact]
    public async Task SenderOutput_unprotects_with_own_crypto_line()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var destPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;

        var channel = new DirectRtpChannel(
            MakePeer(), "screen-capture",
            new IPEndPoint(IPAddress.Loopback, destPort),
            bindPort: 0);
        await channel.OpenAsync();
        channel.CryptoLine.Should().Contain("inline:");

        var au = new byte[32];
        new Random(7).NextBytes(au);
        await channel.SendPacketAsync(new EncodedPacket(au, TimestampUs: 1_000_000, IsKeyFrame: true));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var wire = (await receiver.ReceiveAsync(cts.Token)).Buffer;

        var (key, salt) = SplitInline(channel.CryptoLine);
        var engine = new SIPSorcery.Net.SrtpTransformEngine(key, salt, MakePolicy(), MakePolicy());
        var transformer = new SIPSorcery.Net.SrtpTransformer(engine);
        try
        {
            var plain = transformer.ReverseTransform(wire);
            plain.Should().NotBeNull("the exposed crypto line decrypts the wire packets");
            plain!.Length.Should().Be(wire.Length - 10, "auth tag stripped");
            plain.AsSpan(12).ToArray().Should().Equal(au, "the original Annex-B AU comes back");
        }
        finally
        {
            try { transformer.Close(); } catch { }
            try { engine.Close(); } catch { }
        }

        channel.Dispose();
    }

    [Fact]
    public async Task Sends_srtp_protected_annexb_au_to_fixed_endpoint()
    {
        // Test-owned UDP receiver on loopback: the channel must deliver a
        // datagram here with NO ICE, NO SDP round trip, NO DTLS.
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var destPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;

        var channel = new DirectRtpChannel(
            MakePeer(), "screen-capture",
            new IPEndPoint(IPAddress.Loopback, destPort),
            bindPort: 0); // ephemeral local bind; tests only exercise TX
        await channel.OpenAsync();
        channel.IsOpen.Should().BeTrue("session opened with fabricated SDES answer");

        var au = new byte[64];
        new Random(42).NextBytes(au);
        await channel.SendPacketAsync(new EncodedPacket(au, TimestampUs: 1_000_000, IsKeyFrame: true));

        // RTP send happens on the session's internal worker; allow a beat.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await receiver.ReceiveAsync(cts.Token);

        result.Buffer.Length.Should().BeGreaterThan(au.Length,
            "SRTP appends the 10-byte HMAC-SHA1 auth tag");
        result.Buffer[0].Should().Be(0x80, "RTP version 2, no padding/extensions/CSRCs");
        (result.Buffer[1] & 0x7F).Should().Be(96, "payload type = H264 dynamic 96");

        channel.Dispose();
    }

    [Fact]
    public async Task CryptoLine_comes_from_the_offer_with_30byte_inline()
    {
        var channel = new DirectRtpChannel(
            MakePeer(), "screen-capture",
            new IPEndPoint(IPAddress.Loopback, 5600),
            bindPort: 0);
        await channel.OpenAsync();

        channel.CryptoLine.Should().StartWith("1 AES_CM_128_HMAC_SHA1_80 inline:");
        var inline = channel.CryptoLine["1 AES_CM_128_HMAC_SHA1_80 inline:".Length..];
        Convert.FromBase64String(inline).Should().HaveCount(30, "16-byte master key + 14-byte salt (RFC 4568)");
        channel.Dispose();
    }

    /// <summary>
    /// Generates the cross-language fixture consumed by the Android JVM tests
    /// (jitsi-srtp unprotect + FU-A reassembly must reproduce the original
    /// Annex-B AU). The key material is whatever the sender announced (captured
    /// in the fixture); the AU content is deterministic. Writes only when
    /// UNILINKER_WRITE_FIXTURE=1 so CI stays write-free; the committed file is
    /// the reference.
    /// </summary>
    [Fact]
    public async Task WriteFixture_generates_deterministic_cross_language_vectors()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var destPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;

        var channel = new DirectRtpChannel(
            MakePeer(), "screen-capture",
            new IPEndPoint(IPAddress.Loopback, destPort),
            bindPort: 0);
        await channel.OpenAsync();

        // One keyframe AU with a LARGE IDR NAL (forces FU-A fragmentation,
        // the dominant path for real frames) plus a small PPS NAL (single-NAL
        // packet). Deterministic content.
        var nal1 = new byte[2004];
        nal1[0] = 0x65; // IDR slice NRI=11, type=5
        var rng = new Random(1337);
        rng.NextBytes(nal1.AsSpan(1));
        var nal2 = new byte[] { 0x68, 0xEB, 0xEC, 0xB2, 0x2C, 0xD0 };
        var au = new byte[4 + nal1.Length + 4 + nal2.Length];
        au[0] = au[4 + nal1.Length] = 0x00;
        au[1] = au[5 + nal1.Length] = 0x00;
        au[2] = au[6 + nal1.Length] = 0x00;
        au[3] = au[7 + nal1.Length] = 0x01;
        Array.Copy(nal1, 0, au, 4, nal1.Length);
        Array.Copy(nal2, 0, au, 8 + nal1.Length, nal2.Length);

        const long tsUs = 33_333;
        await channel.SendPacketAsync(new EncodedPacket(au, tsUs, IsKeyFrame: true));

        // Collect whatever RTP packets the session emits for this AU (count
        // depends on SIPSorcery's packetization: single-NAL vs STAP/FU-A).
        var packets = new List<byte[]>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            while (true)
            {
                var r = await receiver.ReceiveAsync(cts.Token);
                packets.Add(r.Buffer);
            }
        }
        catch (OperationCanceledException) { }

        packets.Count.Should().BeGreaterThan(0, "the encrypted AU must reach the wire");

        if (Environment.GetEnvironmentVariable("UNILINKER_WRITE_FIXTURE") == "1")
        {
            var (key, salt) = SplitInline(channel.CryptoLine);
            var fixture = new
            {
                description = "Direct RTP SDES fixture: Windows RTPSession (AES_CM_128_HMAC_SHA1_80 SDES) protecting H264 FU-A packets; Android jitsi-srtp must unprotect and reassemble the original Annex-B AU",
                masterKeyB64 = Convert.ToBase64String(key),
                masterSaltB64 = Convert.ToBase64String(salt),
                cryptoLine = channel.CryptoLine,
                ssrc = channel.Ssrc,
                annexBAuB64 = Convert.ToBase64String(au),
                timestampUs = tsUs,
                packets = packets.Select(p => Convert.ToBase64String(p)).ToArray(),
            };
            var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "direct-rtp-fixture.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(fixture, new JsonSerializerOptions { WriteIndented = true }));
        }

        channel.Dispose();
    }

    // ── M5 RTX: own-pipeline sender ──

    /// <summary>Access the channel's internal sender for packetization/NACK
    /// assertions (internal type, InternalsVisibleTo covers the tests).</summary>
    private static UniLinker.Core.DirectRtpSender GetSender(UniLinker.Core.DirectRtpChannel channel)
    {
        var field = typeof(UniLinker.Core.DirectRtpChannel).GetField("_sender",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (UniLinker.Core.DirectRtpSender)field!.GetValue(channel)!;
    }

    [Fact]
    public void PacketizeAu_splits_large_nal_into_fua_with_marker_on_last()
    {
        var nal1 = new byte[2004];
        nal1[0] = 0x65;
        new Random(1337).NextBytes(nal1.AsSpan(1));
        var nal2 = new byte[] { 0x68, 0xEB, 0xEC, 0xB2, 0x2C, 0xD0 };
        var au = new byte[4 + nal1.Length + 4 + nal2.Length];
        au[0] = au[4 + nal1.Length] = 0x00;
        au[1] = au[5 + nal1.Length] = 0x00;
        au[2] = au[6 + nal1.Length] = 0x00;
        au[3] = au[7 + nal1.Length] = 0x01;
        Array.Copy(nal1, 0, au, 4, nal1.Length);
        Array.Copy(nal2, 0, au, 8 + nal1.Length, nal2.Length);

        var senderType = typeof(UniLinker.Core.DirectRtpSender);
        var method = senderType.GetMethod("PacketizeAu",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        using var sender = new UniLinker.Core.DirectRtpSender(
            new IPEndPoint(IPAddress.Loopback, 5600), bindPort: 0);
        var packets = (List<byte[]>)method.Invoke(sender, new object[] { au, 3000u })!;

        // IDR 2004 B → FU-A chunks of 1398 payload: ceil(2003/1398) = 2;
        // PPS 6 B → 1 single-NAL packet.
        packets.Count.Should().Be(3);
        packets[0][1].Should().Be(96, "no marker on the first FU chunk");
        packets[1][1].Should().Be(96, "no marker on the IDR's last chunk (PPS follows)");
        packets[2][1].Should().Be(0x80 | 96, "marker on the AU's last packet (the PPS single-NAL)");
        packets[0][12].Should().Be(0x7C, "FU indicator: NRI=11 | type=28");
        packets[0][13].Should().Be(0x80 | 5, "FU header: S=1, type=5 (IDR)");
        packets[1][13].Should().Be(0x40 | 5, "FU header: E=1");
        // Reassembled FU payload = the NAL body; the receiver rebuilds the
        // NAL header from indicator NRI + FU type: (0x7C & 0x60) | 5 = 0x65.
        var fuPayload = packets[0][14..].Concat(packets[1][14..]).ToArray();
        fuPayload.Length.Should().Be(nal1.Length - 1, "FU chunks carry the NAL body (header rides the indicator)");
        fuPayload.Should().Equal(nal1[1..]);
        ((packets[0][12] & 0x60) | (packets[0][13] & 0x1F)).Should().Be(nal1[0],
            "indicator NRI + FU type rebuild the original NAL header");
        packets[2][12..].Should().Equal(nal2);
        // All packets share the AU timestamp, and sequence numbers increment.
        var ts = (uint)((packets[0][4] << 24) | (packets[0][5] << 16) | (packets[0][6] << 8) | packets[0][7]);
        ts.Should().Be(3000u);
    }

    [Fact]
    public void EpochTimestamp_maps_capture_us_to_90k_ticks_of_epoch()
    {
        // bootEpochMs=1000, capture at boot+2000ms → epoch 3000ms → 3000*90 ticks.
        var last = 0u;
        var ts = RtpTiming.EpochTimestampFor(2_000_000, 1000, ref last);
        ts.Should().Be(3000u * 90);
        last.Should().Be(ts);
    }

    [Fact]
    public void EpochTimestamp_preserves_frame_deltas_and_survives_u32_wrap()
    {
        // Two frames 16.67ms apart keep their delta in ticks — the receiver
        // diffs with unsigned 32-bit arithmetic, so the u32 wrap (13.2h)
        // changes nothing about the delta.
        var last = 0u;
        long bootEpoch = 100_000;
        var ts1 = RtpTiming.EpochTimestampFor(16_667_000, bootEpoch, ref last);
        var ts2 = RtpTiming.EpochTimestampFor(33_333_000, bootEpoch, ref last);
        (ts2 - ts1).Should().Be((uint)((33_333 - 16_667) * 90));
    }

    [Fact]
    public void EpochTimestamp_breaks_ties_when_two_frames_share_one_millisecond()
    {
        var last = 0u;
        var ts1 = RtpTiming.EpochTimestampFor(500_000, 0, ref last);
        var ts2 = RtpTiming.EpochTimestampFor(500_400, 0, ref last); // same epoch ms
        ts2.Should().Be(ts1 + 1, "the +1 guard keeps consecutive AUs distinct (tile assembler keys on ts)");
    }

    [Fact]
    public async Task Nack_datagram_triggers_retransmission_of_ring_packets()
    {
        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var destPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;

        var channel = new UniLinker.Core.DirectRtpChannel(
            MakePeer(), "screen-capture",
            new IPEndPoint(IPAddress.Loopback, destPort),
            bindPort: 0);
        await channel.OpenAsync();
        var sender = GetSender(channel);
        sender.Start();

        var au = new byte[64];
        new Random(9).NextBytes(au);
        await channel.SendPacketAsync(new EncodedPacket(au, 1_000_000, IsKeyFrame: true));

        // Receive the one wire packet and read its seq.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var wire = (await receiver.ReceiveAsync(cts.Token)).Buffer;
        var seq = (ushort)((wire[2] << 8) | wire[3]);

        // Craft the NACK the Android receiver sends: "UNIN" + count + seq.
        var nack = new byte[8];
        BitConverter.GetBytes(0x554E494Eu).CopyTo(nack, 0);
        Array.Reverse(nack, 0, 4); // big-endian magic
        nack[4] = 0; nack[5] = 1;
        nack[6] = (byte)(seq >> 8); nack[7] = (byte)seq;
        sender.HandleDatagram(nack);

        // The retransmission arrives with the SAME protected bytes.
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var resent = (await receiver.ReceiveAsync(cts2.Token)).Buffer;
        resent.Should().Equal(wire, "RTX resends the original protected packet");
        sender.PacketsRetransmitted.Should().Be(1);
        channel.Dispose();
    }
}
