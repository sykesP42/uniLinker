using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace UniLinker.Core;

/// <summary>
/// Device identity for the pairing/trust model (M4): a self-signed X.509
/// certificate generated on first run and persisted locally. The SHA-256
/// fingerprint of the certificate IS the device identity; the pairing code
/// is a 6-digit SAS (short authentication string) derived from it, shown on
/// BOTH ends for human comparison during first pairing (Matter/HomeKit
/// style). Subsequent connections pin the fingerprint (TOFU).
///
/// The PFX is stored DPAPI-encrypted (current user) — the key never touches
/// disk in plaintext.
/// </summary>
public class DeviceIdentity
{
    public const string DefaultFileName = "device-identity.pfx";

    public X509Certificate2 Certificate { get; }
    public string FingerprintSha256 { get; }
    public string PairingCode { get; }

    private DeviceIdentity(X509Certificate2 certificate)
    {
        Certificate = certificate;
        FingerprintSha256 = ComputeFingerprint(certificate);
        PairingCode = DerivePairingCode(FingerprintSha256);
    }

    /// <summary>Load the persisted identity, or create and persist a new one.</summary>
    public static DeviceIdentity LoadOrCreate(string storeDirectory)
    {
        Directory.CreateDirectory(storeDirectory);
        var pfxPath = Path.Combine(storeDirectory, DefaultFileName);
        if (File.Exists(pfxPath))
        {
            try
            {
                var protectedBytes = File.ReadAllBytes(pfxPath);
                var pfx = System.Security.Cryptography.ProtectedData.Unprotect(
                    protectedBytes, optionalEntropy: null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return new DeviceIdentity(new X509Certificate2(
                    pfx, (string?)null, X509KeyStorageFlags.Exportable));
            }
            catch (Exception ex)
            {
                // Corrupt/unreadable store: regenerate (identity reset).
                Log.Info($"Device identity store unreadable, regenerating: {ex.Message}");
                try { File.Delete(pfxPath); } catch { }
            }
        }

        var identity = Create();
        var exported = identity.Certificate.Export(X509ContentType.Pfx);
        File.WriteAllBytes(pfxPath,
            System.Security.Cryptography.ProtectedData.Protect(exported, optionalEntropy: null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser));
        return identity;
    }

    public static DeviceIdentity Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var name = $"CN=uniLinker {Environment.MachineName}";
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(10));
        // Export/import round-trip so the private key is bound to a
        // persistable certificate handle.
        var pfx = cert.Export(X509ContentType.Pfx);
        return new DeviceIdentity(new X509Certificate2(
            pfx, (string?)null, X509KeyStorageFlags.Exportable));
    }

    /// <summary>SHA-256 fingerprint of the DER encoding, uppercase hex.</summary>
    public static string ComputeFingerprint(X509Certificate2 certificate) =>
        certificate.GetCertHashString(HashAlgorithmName.SHA256);

    /// <summary>
    /// 6-digit SAS code from the fingerprint (HMAC-SHA256 with a fixed label,
    /// folded to 6 decimal digits). Both ends derive the same code from the
    /// same fingerprint — human comparison replaces trusting the network.
    /// </summary>
    public static string DerivePairingCode(string fingerprintSha256Hex)
    {
        using var hmac = new HMACSHA256("uniLinker-pairing-sas"u8.ToArray());
        var digest = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(fingerprintSha256Hex));
        var v = ((digest[0] & 0x7F) << 24) | (digest[1] << 16) | (digest[2] << 8) | digest[3];
        return (v % 1_000_000).ToString("D6");
    }
}
