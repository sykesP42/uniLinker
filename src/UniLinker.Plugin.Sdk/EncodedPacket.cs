namespace UniLinker.Plugin.Sdk;

/// <summary>
/// A single encoded media packet (e.g., H.264 NAL unit) ready for WebRTC transmission.
/// Created by <see cref="IEncoder"/> and consumed by <see cref="IChannel.SendPacketAsync"/>.
/// </summary>
/// <param name="Data">Raw encoded bytes (e.g., H.264 NAL without start code prefix).</param>
/// <param name="TimestampUs">Presentation timestamp in microseconds.</param>
/// <param name="IsKeyFrame">True if this packet is an IDR/sync frame (seek point).</param>
public record EncodedPacket(
    byte[] Data,
    long TimestampUs,
    bool IsKeyFrame);
