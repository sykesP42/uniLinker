using System.Reflection;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace UniLinker.Core.Tests.Probes;

/// <summary>
/// M2-T1 evidence probe: dump SIPSorcery 8.0.12's RTPSession public surface
/// so the M3 direct-RTP spec is grounded in the real API, not assumptions.
/// The spec needs three facts:
///   1. can RTPSession send to a FIXED endpoint without SDP/ICE (SetDestination)?
///   2. can it do SDES-keyed SRTP (SetSecurityDescription) - keys over our
///      HTTP control channel instead of DTLS?
///   3. does it expose H264 send packetization (SendVideoFrame) independent
///      of the WebRTC path?
/// Metadata-only; nothing is invoked.
/// </summary>
public class SipsorceryRtpSessionProbe
{
    private readonly ITestOutputHelper _output;

    public SipsorceryRtpSessionProbe(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Probe_RtpSessionSurface()
    {
        var asm = Assembly.Load("SIPSorcery");

        foreach (var name in new[] { "SIPSorcery.Net.RTPSession", "SIPSorcery.Net.SDPSecurityDescription" })
        {
            var t = asm.GetType(name);
            if (t == null)
            {
                _output.WriteLine("MISSING type: {0}", name);
                continue;
            }

            _output.WriteLine("=== {0} (visible={1}) ===", name, t.IsVisible);
            _output.WriteLine("constructors:");
            foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                _output.WriteLine("  ({0})", string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)));
            }

            _output.WriteLine("methods (declared, public):");
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                             .Where(m => !m.IsSpecialName))
            {
                _output.WriteLine("  {0} {1}({2})", m.ReturnType.Name, m.Name,
                    string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)));
            }

            _output.WriteLine("properties (declared, public):");
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                _output.WriteLine("  {0} {1}", p.PropertyType.Name, p.Name);
            }
            _output.WriteLine("");
        }

        // Hard facts the M3 spec depends on (metadata-level, no invocation).
        var session = asm.GetType("SIPSorcery.Net.RTPSession")!;
        session.Should().NotBeNull();
        var methods = session.GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ToList();
        _output.WriteLine("--- spec-critical facts ---");
        _output.WriteLine("SetDestination: {0}", methods.Contains("SetDestination"));
        _output.WriteLine("SetSecurityDescription: {0}", methods.Contains("SetSecurityDescription"));
        _output.WriteLine("SendVideoFrame: {0}", methods.Contains("SendVideoFrame"));
        _output.WriteLine("SendRTPRaw: {0}", methods.Contains("SendRTPRaw"));

        // M3 implementation needs the RtpSessionConfig property surface.
        var cfg = asm.GetType("SIPSorcery.Net.RtpSessionConfig");
        if (cfg != null)
        {
            _output.WriteLine("=== RtpSessionConfig (visible={0}) ===", cfg.IsVisible);
            foreach (var prop in cfg.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                _output.WriteLine("  property {0} {1}", prop.PropertyType.Name, prop.Name);
            }
            foreach (var f in cfg.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                _output.WriteLine("  field {0} {1}", f.FieldType.Name, f.Name);
            }
        }
    }
}
