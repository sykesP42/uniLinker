using System.Security.Cryptography;

namespace UniLinker.Plugins.FileTransfer.Core;

/// <summary>
/// Provides file hashing utilities for integrity verification.
/// </summary>
public static class FileHasher
{
    /// <summary>
    /// Compute SHA-256 hash of a file.
    /// Returns the hash as a hex string.
    /// </summary>
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();

        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Compute SHA-256 hash of a byte array.
    /// Returns the hash as a hex string.
    /// </summary>
    public static string ComputeSha256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Compute CRC32 of a byte array (quick validation).
    /// </summary>
    public static uint ComputeCrc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc >> 1) ^ (0xEDB88320 & (uint)-(crc & 1));
            }
        }
        return ~crc;
    }

    /// <summary>
    /// Verify a file against an expected hash.
    /// </summary>
    public static async Task<bool> VerifyFileAsync(string filePath, string expectedHash, CancellationToken cancellationToken = default)
    {
        var actualHash = await ComputeSha256Async(filePath, cancellationToken);
        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
