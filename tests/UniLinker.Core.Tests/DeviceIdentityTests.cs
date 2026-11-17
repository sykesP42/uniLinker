using FluentAssertions;
using UniLinker.Core;
using Xunit;

namespace UniLinker.Core.Tests;

/// <summary>
/// M4 pairing foundation: device identity (self-signed cert) — fingerprint
/// stability across reloads, SAS derivation determinism, DPAPI-protected
/// persistence.
/// </summary>
public class DeviceIdentityTests : IDisposable
{
    private readonly string _storeDir;

    public DeviceIdentityTests()
    {
        _storeDir = Path.Combine(Path.GetTempPath(), "unilinker-idtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storeDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_storeDir, true); } catch { }
    }

    [Fact]
    public void Create_produces_cert_with_64hex_fingerprint_and_6digit_code()
    {
        var id = DeviceIdentity.Create();

        id.FingerprintSha256.Should().HaveLength(64);
        id.FingerprintSha256.Should().MatchRegex("^[0-9A-F]{64}$");
        id.PairingCode.Should().HaveLength(6);
        id.PairingCode.Should().MatchRegex("^\\d{6}$");
        id.Certificate.Subject.Should().Contain("uniLinker");
    }

    [Fact]
    public void LoadOrCreate_persists_and_keeps_fingerprint_stable()
    {
        var first = DeviceIdentity.LoadOrCreate(_storeDir);
        var fp1 = first.FingerprintSha256;
        var code1 = first.PairingCode;

        File.Exists(Path.Combine(_storeDir, DeviceIdentity.DefaultFileName)).Should().BeTrue();

        var second = DeviceIdentity.LoadOrCreate(_storeDir);
        second.FingerprintSha256.Should().Be(fp1, "identity survives restarts");
        second.PairingCode.Should().Be(code1);
        second.Certificate.Thumbprint.Should().Be(first.Certificate.Thumbprint);
    }

    [Fact]
    public void DerivePairingCode_is_deterministic_and_sensitive_to_fingerprint()
    {
        var fpA = new string('A', 64);
        var fpB = new string('B', 64);

        DeviceIdentity.DerivePairingCode(fpA).Should().Be(DeviceIdentity.DerivePairingCode(fpA));
        DeviceIdentity.DerivePairingCode(fpA).Should().NotBe(DeviceIdentity.DerivePairingCode(fpB));
    }

    [Fact]
    public void LoadOrCreate_regenerates_when_store_is_corrupt()
    {
        var pfxPath = Path.Combine(_storeDir, DeviceIdentity.DefaultFileName);
        File.WriteAllBytes(pfxPath, new byte[] { 1, 2, 3, 4 }); // garbage

        var id = DeviceIdentity.LoadOrCreate(_storeDir);

        id.FingerprintSha256.Should().HaveLength(64, "corrupt store regenerates a fresh identity");
    }
}
