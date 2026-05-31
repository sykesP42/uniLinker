namespace UniLinker.Plugin.Sdk;

public interface IEncoder : IDisposable
{
    event Action<EncodedPacket>? PacketEncoded;
    bool Initialize(int width, int height, int fps, int bitrateKbps);
    void Encode(CaptureFrame frame);
    EncoderInfo GetInfo();
}

public record EncoderInfo(string CodecName, int BitrateKbps, bool IsHardware);
