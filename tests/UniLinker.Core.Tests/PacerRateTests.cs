using System.Net;
using System.Diagnostics;
using UniLinker.Core;
using Xunit;

namespace UniLinker.Core.Tests;

/// <summary>
/// Pacer rate regression gate. The token bucket must drain at its configured
/// budget (1.1× tier for WiFi/LAN); every real bug so far showed up here
/// first as a silently throttled wire — the Sleep(2)-granularity collapse
/// (~650pps when the app window lost focus, 2026-11-21) and the missing
/// credit wait (unbounded negative credit, 27Mbps on an 8.8Mbps budget)
/// both failed this shape of measurement on-device before it existed.
/// Sends go to a closed loopback port: UDP is fire-and-forget, the loop
/// times nothing but the pacer itself.
/// </summary>
public class PacerRateTests
{
    [Fact]
    public void TilePacer_drains_within_2x_of_budget()
    {
        const int bitrateKbps = 32000;
        const int chunkLen = 1393;                  // ChunkBudget (wire payload)
        // A real tile blob: u16 BE length prefixes + payloads. A zero-filled
        // blob would mean len=0 chunks (2B steps, thousands of 12B packets) —
        // not the wire shape the pacer must handle.
        const int chunks = 93;
        var blob = new byte[chunks * (2 + chunkLen)];
        for (int i = 0; i < chunks; i++)
        {
            var pos = i * (2 + chunkLen);
            blob[pos] = (byte)(chunkLen >> 8);
            blob[pos + 1] = (byte)(chunkLen & 0xFF);
        }
        var sender = new DirectRtpSender(
            new IPEndPoint(IPAddress.Loopback, 53001), 55001, bitrateKbps, tileMode: true);
        try
        {
            sender.Start();
            var expectedPackets = chunks;
            var expectedMs = blob.Length / (bitrateKbps * 11 / 80.0); // budget B/ms

            // Warmup frame: the first pass JITs the SRTP Transform path
            // (a one-off ~40ms the steady state never pays again). Same wire
            // shape as the measured frame (a zero-filled blob would mean
            // len=0 chunks — thousands of 12B packets).
            sender.SendTileAu(blob, 2, isKeyFrame: true);
            var deadline = Stopwatch.StartNew();
            while (sender.PacketsSent < expectedPackets && deadline.ElapsedMilliseconds < 2000)
                Thread.Sleep(2);

            var before = sender.PacketsSent;
            var sw = Stopwatch.StartNew();
            sender.SendTileAu(blob, 1, isKeyFrame: true);
            while (sender.PacketsSent - before < expectedPackets && sw.ElapsedMilliseconds < 1000)
                Thread.Sleep(2);
            sw.Stop();
            var sent = sender.PacketsSent - before;

            Assert.Equal(expectedPackets, sent);
            // 4x gate = bucket (1x) + serial SRTP Transform (~0.6x, 0.19ms per
            // packet, measured) + GC/scheduling variance on a loaded box. The
            // gate exists to catch ORDER-OF-MAGNITUDE throttling regressions
            // (the Sleep-granularity collapse was 8x slower than budget).
            Assert.True(sw.ElapsedMilliseconds < expectedMs * 4.0,
                $"pacer drained {blob.Length}B in {sw.ElapsedMilliseconds}ms — budget+transform allows " +
                $"~{expectedMs * 1.6:F1}ms (4x gate). sent={sent}.");
        }
        finally
        {
            sender.Dispose();
        }
    }
}
