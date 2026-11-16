using System.Text.Json;
using FluentAssertions;
using UniLinker.Core;
using Xunit;

namespace UniLinker.Core.Tests;

/// <summary>
/// Service-level tests for SignalingService — the transport-free extraction
/// of the old SignalingServer's business logic (M2 strangler migration,
/// docs/spec/M2-kestrel.md). The JSON shapes here must stay identical to
/// the old TcpListener server's responses; the Kestrel host is a pure
/// transport mapping on top.
/// </summary>
public class SignalingServiceTests
{
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static SignalingService CreateService(int port = 9527) =>
        new(new PeerMesh(), port, new[] { "screen-capture", "h264-encode", "video-stream" });

    [Fact]
    public void GetInfo_matches_legacy_contract_shape()
    {
        var service = CreateService();

        var json = JsonSerializer.Serialize(service.GetInfo(), Wire);

        json.Should().Contain("\"name\":");
        json.Should().Contain("\"version\":\"3.1.0\"");
        json.Should().Contain("\"capabilities\":[\"screen-capture\",\"h264-encode\",\"video-stream\"]");
        json.Should().Contain("\"status\":\"available\"");
        json.Should().NotContain("DeviceName").And.NotContain("Capabilities");
    }

    [Fact]
    public void GetStatus_matches_legacy_contract_shape_and_reflects_running_state()
    {
        var service = CreateService(port: 9601);

        var running = JsonSerializer.Serialize(service.GetStatus(isRunning: true), Wire);
        var stopped = JsonSerializer.Serialize(service.GetStatus(isRunning: false), Wire);

        running.Should().Contain("\"port\":9601");
        running.Should().Contain("\"peers\":0");
        running.Should().Contain("\"status\":\"running\"");
        stopped.Should().Contain("\"status\":\"stopped\"");
    }

    [Fact]
    public async Task HandleOfferAsync_returns_answer_with_legacy_shape_and_registers_peer()
    {
        var service = CreateService();
        var offer = new SignalingMessage
        {
            Type = "offer",
            // Minimal SDP; ICE gathering falls back after its 3s timeout, so
            // this must return deterministically without any network.
            Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\n",
            FromPeerId = "test-android-1",
            Capability = "screen-capture",
        };

        SignalingMessage? answer = await service.HandleOfferAsync(offer);

        answer.Should().NotBeNull("a successfully initialized offer yields an answer");
        answer!.Type.Should().Be("answer");
        answer.FromPeerId.Should().Be(Environment.MachineName);
        answer.Capability.Should().BeNull("the legacy answer leaves Capability unset");
        service.PendingIceCount.Should().Be(1, "the negotiated connection is tracked until closed");
    }

    [Fact]
    public async Task HandleOfferAsync_with_empty_peer_id_uses_unknown()
    {
        var service = CreateService();
        var offer = new SignalingMessage
        {
            Type = "offer",
            Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\n",
            FromPeerId = "",
        };

        var answer = await service.HandleOfferAsync(offer);

        answer.Should().NotBeNull();
        // Peer registered under the legacy "unknown" fallback id.
        service.PeerIds.Should().Contain("unknown");
    }

    [Fact]
    public async Task HandleIceCandidateAsync_for_unknown_peer_is_silently_ignored()
    {
        var service = CreateService();

        // Must not throw for a peer that never offered.
        await service.HandleIceCandidateAsync(new IceCandidateMessage
        {
            PeerId = "ghost-peer",
            Candidate = "candidate:1 1 UDP 2122252543 192.168.1.1 9999 typ host",
            SdpMid = "0",
            SdpMLineIndex = 0,
        });
    }

    [Fact]
    public async Task Stop_disposes_pending_connections()
    {
        var service = CreateService();
        var offer = new SignalingMessage
        {
            Type = "offer",
            Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\n",
            FromPeerId = "test-android-2",
            Capability = "screen-capture",
        };
        await service.HandleOfferAsync(offer);
        service.PendingIceCount.Should().Be(1);

        service.Stop();

        service.PendingIceCount.Should().Be(0, "Stop disposes and clears pending negotiated connections");
    }
}
