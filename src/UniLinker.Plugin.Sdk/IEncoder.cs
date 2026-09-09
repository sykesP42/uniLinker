namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Video/audio encoder interface. Implementations convert raw capture frames
/// into compressed packets suitable for WebRTC transmission.
/// </summary>
public interface IEncoder : IDisposable
{
    /// <summary>Raised each time an encoded packet is ready for transmission.</summary>
    event Action<EncodedPacket>? PacketEncoded;

    /// <summary>
    /// Initialize the encoder with the given resolution and bitrate.
    /// </summary>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="fps">Target frames per second.</param>
    /// <param name="bitrateKbps">Target bitrate in kbps.</param>
    /// <returns>True if initialization succeeded.</returns>
    bool Initialize(int width, int height, int fps, int bitrateKbps);

    /// <summary>Encode a single captured frame. Raises <see cref="PacketEncoded"/> for each output packet.</summary>
    void Encode(CaptureFrame frame);

    /// <summary>Return metadata about this encoder (codec, bitrate, hardware acceleration).</summary>
    EncoderInfo GetInfo();
}

/// <summary>Metadata describing an encoder's capabilities.</summary>
public record EncoderInfo(string CodecName, int BitrateKbps, bool IsHardware);
