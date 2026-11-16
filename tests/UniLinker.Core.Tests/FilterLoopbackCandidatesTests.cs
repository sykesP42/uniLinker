using FluentAssertions;
using UniLinker.Core;
using Xunit;

namespace UniLinker.Core.Tests;

/// <summary>
/// Characterization tests for SignalingService.FilterLoopbackCandidates.
/// These lock behavior that was introduced to fix the M0/G1 "zero STUN
/// packets" ICE failure (SIPSorcery pairing loopback candidates from
/// libwebrtc peers). The \r\n normalization matters: an earlier naive
/// Split/Join produced "\r\r\n" sequences that mangled the SDP.
/// </summary>
public class FilterLoopbackCandidatesTests
{
    private const string HostCandidateReal =
        "a=candidate:1 1 UDP 2122252543 192.168.137.1 9527 typ host";
    private const string HostCandidateLoopback =
        "a=candidate:2 1 UDP 2122252543 127.0.0.1 60436 typ host";
    private const string HostCandidateLoopbackV6 =
        "a=candidate:3 1 UDP 2122252543 ::1 60437 typ host";

    private static string MinimalSdp(params string[] candidateLines) =>
        "v=0\r\no=- 1 1 IN IP4 127.0.0.1\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\n"
        + string.Join("\r\n", candidateLines);

    [Fact]
    public void Keeps_real_host_candidate_and_strips_loopback_ones()
    {
        var sdp = MinimalSdp(HostCandidateReal, HostCandidateLoopback, HostCandidateLoopbackV6);

        var result = SignalingService.FilterLoopbackCandidates(sdp);

        result.Should().Contain("192.168.137.1");
        result.Should().NotContain("127.0.0.1 60436");
        result.Should().NotContain("::1 60437");
    }

    [Fact]
    public void Strips_all_candidate_lines_when_every_candidate_is_loopback()
    {
        var sdp = MinimalSdp(HostCandidateLoopback, HostCandidateLoopbackV6);

        var result = SignalingService.FilterLoopbackCandidates(sdp);

        result.Split("\r\n").Should().NotContain(l => l.TrimStart().StartsWith("a=candidate:"));
        // Non-candidate SDP lines survive so the description stays parseable.
        result.Should().Contain("m=video");
        result.Should().Contain("v=0");
    }

    [Fact]
    public void Normalizes_lf_only_input_to_crlf_without_double_cr()
    {
        // The regression: naive Split('\n')/Join("\r\n") over CRLF input left
        // "\r\r\n" pairs on the wire, breaking SDP parsing downstream.
        var sdp = "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\n" + HostCandidateReal + "\r\n";

        var result = SignalingService.FilterLoopbackCandidates(sdp);

        result.Should().NotContain("\r\r");
        result.Split("\r\n").Should().HaveCount(4).And.Contain(HostCandidateReal);
    }

    [Fact]
    public void Handles_lf_only_input()
    {
        var sdp = "v=0\nm=video 9 UDP/TLS/RTP/SAVPF 96\n" + HostCandidateLoopback + "\n";

        var result = SignalingService.FilterLoopbackCandidates(sdp);

        result.Should().NotContain("127.0.0.1");
        result.Should().Contain("m=video");
        result.Should().NotContain("\r\r");
    }

    [Fact]
    public void Preserves_line_order_and_count_for_non_candidate_lines()
    {
        var sdp = "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\n"
                  + HostCandidateLoopback + "\r\n"
                  + "a=rtpmap:96 H264/90000\r\n";

        var result = SignalingService.FilterLoopbackCandidates(sdp);

        result.Should().Contain("a=rtpmap:96 H264/90000");
        result.Should().NotContain(HostCandidateLoopback);
    }

    [Fact]
    public void Empty_input_returns_empty_output()
    {
        SignalingService.FilterLoopbackCandidates("").Should().Be("");
    }
}
