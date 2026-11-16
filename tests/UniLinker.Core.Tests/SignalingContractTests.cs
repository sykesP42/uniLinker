using System.Text.Json;
using FluentAssertions;
using UniLinker.Core;
using Xunit;

namespace UniLinker.Core.Tests;

/// <summary>
/// Characterization tests for the /signaling and /ice wire contract.
/// Both the Android app (WebRTCService.kt) and the browser viewer (web/app.js)
/// depend on these exact JSON shapes; breaking them silently kills every
/// client at once. The server serializes AND deserializes with
/// JsonNamingPolicy.CamelCase (now SignalingService + SignalingKestrelHost;
/// HandleSignalingAsync).
/// </summary>
public class SignalingContractTests
{
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ---- Offer: what Android (OkHttp) and the browser POST ----

    [Fact]
    public void Deserializes_android_offer_payload_with_camel_case_and_extra_fields()
    {
        // WebRTCService.kt sendSdp() sends fromPeerId AND an extra peerId field;
        // the server DTO has no PeerId property, so unknown fields must be ignored.
        const string json = """
            {"type":"offer","sdp":"v=0\r\nm=video 9","fromPeerId":"android-1",
             "capability":"screen-capture","peerId":"android-1"}
            """;

        var msg = JsonSerializer.Deserialize<SignalingMessage>(json, Wire);

        msg.Should().NotBeNull();
        msg!.Type.Should().Be("offer");
        msg.Sdp.Should().Contain("m=video");
        msg.FromPeerId.Should().Be("android-1");
        msg.Capability.Should().Be("screen-capture");
    }

    [Fact]
    public void Deserializes_browser_offer_payload()
    {
        // web/app.js posts {type:'offer', sdp, fromPeerId:'browser-<ts>-<rand>',
        // capability:'screen-capture'} - no peerId field.
        const string json = """
            {"type":"offer","sdp":"v=0","fromPeerId":"browser-1731-abc","capability":"screen-capture"}
            """;

        var msg = JsonSerializer.Deserialize<SignalingMessage>(json, Wire);

        msg!.FromPeerId.Should().Be("browser-1731-abc");
        msg.Capability.Should().Be("screen-capture");
    }

    // ---- Answer: what the server returns in the HTTP response ----

    [Fact]
    public void Serializes_answer_with_camel_case_keys_clients_depend_on()
    {
        // Android reads answerJson.getString("type") and .getString("sdp");
        // the browser reads the same shape. Key names are the contract.
        var answer = new SignalingMessage { Type = "answer", Sdp = "v=0\r\nm=video 9", FromPeerId = "DESKTOP-1", Capability = "screen-capture" };

        var json = JsonSerializer.Serialize(answer, Wire);

        json.Should().Contain("\"type\":\"answer\"");
        json.Should().Contain("\"sdp\":");
        json.Should().Contain("\"fromPeerId\":\"DESKTOP-1\"");
        json.Should().Contain("\"capability\":\"screen-capture\"");
        json.Should().NotContain("FromPeerId").And.NotContain("Sdp");
    }

    [Fact]
    public void Serializes_null_capability_as_absent_or_null_not_string()
    {
        var msg = new SignalingMessage { Type = "answer", Sdp = "v=0", FromPeerId = "DESKTOP-1" };

        var json = JsonSerializer.Serialize(msg, Wire);

        json.Should().Contain("\"capability\":null");
        json.Should().NotContain("\"capability\":\"null\"");
    }

    // ---- /ice candidate payload ----

    [Fact]
    public void Deserializes_android_ice_candidate_payload()
    {
        // WebRTCService.kt sendIceCandidate() sends {type, candidate, sdpMid,
        // sdpMLineIndex, peerId} - Type is not on the DTO and must be ignored.
        const string json = """
            {"type":"candidate","candidate":"candidate:842163049 1 udp 1677729535 192.168.137.1 60436 typ host",
             "sdpMid":"0","sdpMLineIndex":0,"peerId":"android-1"}
            """;

        var msg = JsonSerializer.Deserialize<IceCandidateMessage>(json, Wire);

        msg.Should().NotBeNull();
        msg!.PeerId.Should().Be("android-1");
        msg.Candidate.Should().StartWith("candidate:842163049");
        msg.SdpMid.Should().Be("0");
        msg.SdpMLineIndex.Should().Be(0);
    }

    [Fact]
    public void Round_trips_ice_candidate_message()
    {
        var original = new IceCandidateMessage
        {
            PeerId = "browser-1",
            Candidate = "candidate:1 1 UDP 2122252543 192.168.137.1 9527 typ host",
            SdpMid = "0",
            SdpMLineIndex = 0,
        };

        var json = JsonSerializer.Serialize(original, Wire);
        var restored = JsonSerializer.Deserialize<IceCandidateMessage>(json, Wire);

        restored.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Ice_candidate_json_uses_camel_case_keys()
    {
        var msg = new IceCandidateMessage { PeerId = "p1", Candidate = "candidate:x", SdpMid = "1", SdpMLineIndex = 2 };

        var json = JsonSerializer.Serialize(msg, Wire);

        json.Should().Contain("\"peerId\":\"p1\"");
        json.Should().Contain("\"sdpMid\":\"1\"");
        json.Should().Contain("\"sdpMLineIndex\":2");
    }
}
