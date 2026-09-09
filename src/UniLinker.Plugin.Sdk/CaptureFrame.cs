namespace UniLinker.Plugin.Sdk;

/// <summary>
/// A single captured frame from <see cref="ICapture"/>.
/// Contains either a D3D texture handle (zero-copy path) or raw pixel data.
/// </summary>
/// <param name="D3dTexture">Native D3D11 texture pointer (0 if using raw data path).</param>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="Pitch">Row stride in bytes (for raw data).</param>
/// <param name="TimestampUs">Capture timestamp in microseconds.</param>
/// <param name="RawData">Raw BGRA pixel data (null when using D3D texture path).</param>
public record CaptureFrame(
    nint D3dTexture,
    int Width,
    int Height,
    int Pitch,
    long TimestampUs,
    byte[]? RawData = null);
