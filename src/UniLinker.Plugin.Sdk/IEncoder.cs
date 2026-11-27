namespace UniLinker.Plugin.Sdk;

public interface IEncoder : IDisposable
{
    event Action<EncodedPacket>? PacketEncoded;
    /// <summary>Open the encoder. The optional D3D11 pointers (device +
    /// immediate context, borrowed from the capture, plus the shared GPU
    /// gate that serializes context use) enable the zero-copy path: the
    /// encoder then consumes CaptureFrame.D3dTexture instead of RawData.
    /// Implementations that cannot use them ignore the pointers and keep
    /// consuming RawData.</summary>
    bool Initialize(int width, int height, int fps, int bitrateKbps,
        nint sharedDevicePtr = 0, nint sharedContextPtr = 0, object? gpuGate = null);
    void Encode(CaptureFrame frame);
    /// <summary>Request an IDR on the next emitted frame (viewer join /
    /// packet-loss recovery). Must be safe to call from any thread.</summary>
    void ForceKeyFrame();
    EncoderInfo GetInfo();
}

public record EncoderInfo(string CodecName, int BitrateKbps, bool IsHardware);
