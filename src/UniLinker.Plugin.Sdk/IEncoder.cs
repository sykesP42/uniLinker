namespace UniLinker.Plugin.Sdk;

public interface IEncoder : IDisposable
{
    event Action<EncodedPacket>? PacketEncoded;
    bool Initialize(int width, int height, int fps, int bitrateKbps);
    void Encode(CaptureFrame frame);
    /// <summary>Request an IDR on the next emitted frame (viewer join /
    /// packet-loss recovery). Must be safe to call from any thread.</summary>
    void ForceKeyFrame();
    EncoderInfo GetInfo();
}

public record EncoderInfo(string CodecName, int BitrateKbps, bool IsHardware);
