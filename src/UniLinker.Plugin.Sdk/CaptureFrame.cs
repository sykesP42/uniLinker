namespace UniLinker.Plugin.Sdk;

public record CaptureFrame(
    nint D3dTexture,
    int Width,
    int Height,
    int Pitch,
    long TimestampUs);
