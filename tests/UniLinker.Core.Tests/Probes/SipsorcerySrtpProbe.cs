using System.Reflection;
using System.Security.Cryptography;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace UniLinker.Core.Tests.Probes;

/// <summary>
/// P2 probe (docs/spec/M1-probes.md): can SIPSorcery 8.0.12's SRTP
/// implementation be reused WITHOUT a DTLS handshake, keyed from our own
/// key material? If yes, the "direct RTP" transport tier needs zero new
/// crypto code; if no, we fall to the decision tree (P6 libdatachannel).
///
/// v2 of the probe: the initially-assumed type name "DtlsSrtp" does NOT
/// exist in 8.0.12 (the earlier grep hit was the DtlsSrtpClient/Server/
/// Transport family). The probe therefore classifies every SRTP-related
/// type by the same rule: public type + public Protect*/Unprotect* entry
/// points + a public constructor (or settable key property) whose
/// parameters are all keyable material — i.e. constructible without a
/// DTLS transport.
///
/// Metadata-only for non-public members (per spec: never invoked). The
/// verdict is written to the test output; docs/redesign/09 §10 records it.
/// </summary>
public class SipsorcerySrtpProbe
{
    private readonly ITestOutputHelper _output;

    public SipsorcerySrtpProbe(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Probe_SipsorcerySrtpReuseFeasibility()
    {
        Assembly asm;
        try
        {
            asm = Assembly.Load("SIPSorcery");
        }
        catch (Exception ex)
        {
            _output.WriteLine("VERDICT: NOT FEASIBLE (SIPSorcery assembly not loadable: {0})", ex.Message);
            throw;
        }

        _output.WriteLine("SIPSorcery assembly: {0}", asm.FullName);

        Type[] srtpTypes;
        try
        {
            srtpTypes = asm.GetTypes()
                .Where(t => t.Name.Contains("Srtp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.FullName)
                .ToArray();
        }
        catch (ReflectionTypeLoadException ex)
        {
            srtpTypes = ex.Types.Where(t => t != null).Cast<Type>()
                .Where(t => t.Name.Contains("Srtp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.FullName)
                .ToArray();
        }

        srtpTypes.Should().NotBeEmpty("the 8.0.12 metadata contains SRTP types (grepped: ProtectRTP/UnprotectRTP strings)");

        var candidates = new List<(Type Type, bool Keyable, bool Protect, bool Unprotect)>();

        foreach (var t in srtpTypes)
        {
            _output.WriteLine("=== {0} | visible={1} ===", t.FullName, t.IsVisible);
            DumpConstructors(t);
            DumpKeyMethods(t);

            if (!t.IsVisible) continue;

            var protect = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(m => m.Name.StartsWith("Protect", StringComparison.OrdinalIgnoreCase));
            var unprotect = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(m => m.Name.StartsWith("Unprotect", StringComparison.OrdinalIgnoreCase));
            var keyableCtor = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Any(c => c.GetParameters().All(IsKeyableParameter));
            var keyableProps = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(p => p.PropertyType == typeof(byte[]) && p.CanWrite);

            if (protect && unprotect && (keyableCtor || keyableProps))
            {
                candidates.Add((t, keyableCtor || keyableProps, protect, unprotect));
            }
        }

        _output.WriteLine("--- verdict ---");
        if (candidates.Count == 0)
        {
            _output.WriteLine("VERDICT: NOT FEASIBLE (no public SRTP type offers public Protect/Unprotect + externally keyable construction)");
        }
        else
        {
            foreach (var (t, keyable, _, _) in candidates)
            {
                _output.WriteLine("VERDICT: FEASIBLE-CANDIDATE type={0} keyableConstruction={1}", t.FullName, keyable);
                DumpKeyMaterialEntryPoints(t);
            }
        }

        // Full public surfaces of the wire-usable types + policy constants,
        // so a protect/unprotect round-trip can be built from evidence.
        _output.WriteLine("--- policy constants ---");
        var policyType = asm.GetType("SIPSorcery.Net.SrtpPolicy");
        if (policyType != null)
        {
            foreach (var f in policyType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                _output.WriteLine("  const {0} {1} = {2}", f.FieldType.Name, f.Name, f.GetRawConstantValue());
            }
        }

        foreach (var name in new[] { "SIPSorcery.Net.SrtpHandler", "SIPSorcery.Net.SrtpTransformEngine", "SIPSorcery.Net.SrtpTransformer" })
        {
            var t = asm.GetType(name);
            if (t == null) continue;
            _output.WriteLine("--- full surface: {0} ---", name);
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                _output.WriteLine("  {0} {1}({2})", m.ReturnType.Name, m.Name,
                    string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)));
            }
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                _output.WriteLine("  property {0} {1} {{ {2} }}", p.PropertyType.Name, p.Name,
                    (p.CanRead ? "get;" : "") + (p.CanWrite ? "set;" : ""));
            }
        }
    }

    /// <summary>
    /// Decision-grade P2 evidence: build an SRTP engine from OUR OWN key
    /// material (16-byte master key + 14-byte salt, standard AES-CM/HMAC-SHA1
    /// policy — the WebRTC-default profile) and round-trip a synthetic RTP
    /// packet through protect/unprotect. No DTLS handshake involved anywhere.
    /// Uses compile-time references, proving the types are publicly consumable
    /// as a dependency (not just reflectable).
    /// </summary>
    [Fact]
    public void Probe_SrtpRoundTrip_WithExternalKeys_WithoutDtls()
    {
        byte[] masterKey = RandomNumberGenerator.GetBytes(16);
        byte[] masterSalt = RandomNumberGenerator.GetBytes(14);

        var srtpPolicy = new SIPSorcery.Net.SrtpPolicy(
            SIPSorcery.Net.SrtpPolicy.AESCM_ENCRYPTION, 16,
            SIPSorcery.Net.SrtpPolicy.HMACSHA1_AUTHENTICATION, 20,
            authTagLength: 10, saltKeyLength: 14);
        var srtcpPolicy = new SIPSorcery.Net.SrtpPolicy(
            SIPSorcery.Net.SrtpPolicy.AESCM_ENCRYPTION, 16,
            SIPSorcery.Net.SrtpPolicy.HMACSHA1_AUTHENTICATION, 20,
            authTagLength: 10, saltKeyLength: 14);

        var engine = new SIPSorcery.Net.SrtpTransformEngine(masterKey, masterSalt, srtpPolicy, srtcpPolicy);
        var transformer = new SIPSorcery.Net.SrtpTransformer(engine);
        try
        {
            byte[] rtpPacket = BuildRtpPacket(ssrc: 0x22222222u, seq: 0x1234, payloadBytes: 16);

            byte[]? protectedPacket = transformer.Transform(rtpPacket);

            protectedPacket.Should().NotBeNull("ProtectRTP transforms the packet");
            protectedPacket!.Length.Should().BeGreaterThan(rtpPacket.Length,
                "SRTP appends the 10-byte HMAC-SHA1 auth tag");

            byte[]? roundTripped = transformer.ReverseTransform(protectedPacket);

            roundTripped.Should().NotBeNull("UnprotectRTP restores the packet");
            roundTripped!.Should().Equal(rtpPacket,
                "the exact original RTP packet comes back with our own keys, no DTLS");
        }
        finally
        {
            try { transformer.Close(); } catch { }
            try { engine.Close(); } catch { }
        }
    }

    private static byte[] BuildRtpPacket(uint ssrc, ushort seq, int payloadBytes)
    {
        var packet = new byte[12 + payloadBytes];
        packet[0] = 0x80;                       // V=2, P=0, X=0, CC=0
        packet[1] = 0x60;                       // PT=96 (dynamic, H264), M=0
        packet[2] = (byte)(seq >> 8);
        packet[3] = (byte)seq;
        packet[4] = 0x11; packet[5] = 0x22; packet[6] = 0x33; packet[7] = 0x44;   // timestamp
        packet[8] = (byte)(ssrc >> 24); packet[9] = (byte)(ssrc >> 16);
        packet[10] = (byte)(ssrc >> 8); packet[11] = (byte)ssrc;
        RandomNumberGenerator.Fill(packet.AsSpan(12));
        return packet;
    }

    /// <summary>A parameter we could plausibly supply ourselves: primitives,
    /// byte[], string, enums — as opposed to transport objects (SslStream,
    /// Stream, RTCPeerConnection, certificates, delegates).</summary>
    private static bool IsKeyableParameter(ParameterInfo p)
    {
        var t = p.ParameterType;
        if (t.IsByRef) t = t.GetElementType()!;
        return t == typeof(bool) || t == typeof(int) || t == typeof(uint) || t == typeof(string)
               || t == typeof(byte[]) || t.IsEnum
               || (t.IsArray && t.GetElementType() == typeof(byte[]));
    }

    private void DumpConstructors(Type t)
    {
        foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            _output.WriteLine("  ctor {0} ({1})", c.IsPublic ? "public" : "nonpublic",
                string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)));
        }
    }

    private void DumpKeyMethods(Type t)
    {
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         .Where(m => m.Name.Contains("Protect", StringComparison.OrdinalIgnoreCase)
                                  || m.Name.Contains("Unprotect", StringComparison.OrdinalIgnoreCase)
                                  || m.Name.Contains("Key", StringComparison.OrdinalIgnoreCase)
                                  || m.Name.Contains("Salt", StringComparison.OrdinalIgnoreCase)
                                  || m.Name.Contains("Master", StringComparison.OrdinalIgnoreCase)))
        {
            _output.WriteLine("  {0} {1} {2}({3})", m.IsPublic ? "public" : "nonpublic", m.ReturnType.Name,
                m.Name, string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)));
        }
    }

    private void DumpKeyMaterialEntryPoints(Type t)
    {
        _output.WriteLine("  --- key-material entry points ---");
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (m.GetParameters().Any(p => p.ParameterType == typeof(byte[])))
            {
                _output.WriteLine("  method {0}({1})", m.Name,
                    string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)));
            }
        }
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.PropertyType == typeof(byte[]) && p.CanWrite)
            {
                _output.WriteLine("  settable property byte[] {0}", p.Name);
            }
        }
    }
}
