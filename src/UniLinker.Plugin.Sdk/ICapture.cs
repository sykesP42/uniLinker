namespace UniLinker.Plugin.Sdk;

/// <summary>
/// Screen capture interface. Implementations provide raw or D3D texture frames
/// from the desktop at the requested resolution and frame rate.
/// </summary>
public interface ICapture : IDisposable
{
    /// <summary>Raised each time a new frame is captured.</summary>
    event Action<CaptureFrame>? FrameCaptured;

    /// <summary>
    /// Start capturing at the given resolution and frame rate.
    /// </summary>
    /// <param name="width">Desired frame width (backend may use nearest supported).</param>
    /// <param name="height">Desired frame height.</param>
    /// <param name="fps">Desired frame rate.</param>
    /// <returns>True if capture started successfully.</returns>
    bool Start(int width, int height, int fps);

    /// <summary>Stop capturing and release frame resources.</summary>
    void Stop();

    /// <summary>Return metadata about the active capture session.</summary>
    CaptureInfo GetInfo();
}

/// <summary>Metadata describing an active capture session.</summary>
public record CaptureInfo(int Width, int Height, int Fps, string Backend);
