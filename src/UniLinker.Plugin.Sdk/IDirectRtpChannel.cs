namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Public view of a direct-RTP channel for plugins (C-1: extend via new
/// interfaces, never break IChannel). Exposes the capture parameters the
/// RECEIVER requested, so the capture plugin can start the session at the
/// tier the receiver actually wants. Implemented by UniLinker.Core's
/// DirectRtpChannel; the plain IChannel contract is unchanged.
/// </summary>
public interface IDirectRtpChannel : IChannel
{
    /// <summary>Capture width the receiver requested (0 = sender default).</summary>
    int RequestedWidth { get; }

    /// <summary>Capture height the receiver requested (0 = sender default).</summary>
    int RequestedHeight { get; }

    /// <summary>Bitrate the receiver requested, kbps (0 = sender default).</summary>
    int RequestedBitrateKbps { get; }

    /// <summary>Frame rate the receiver requested (0 = sender default).</summary>
    int RequestedFps { get; }

    /// <summary>Bind + start the sender (SDES setup for the legacy session,
    /// socket bind for the RTX sender). Throws on port conflicts.</summary>
    Task OpenAsync();

    /// <summary>The SDES crypto attribute the receiver unprotects with
    /// ("1 AES_CM_128_HMAC_SHA1_80 inline:base64(key16+salt14)").</summary>
    string CryptoLine { get; }

    /// <summary>The stream SSRC.</summary>
    uint Ssrc { get; }
}
