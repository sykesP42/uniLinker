namespace UniLinker.Plugin.Sdk;

public interface ICapture : IDisposable
{
    event Action<CaptureFrame>? FrameCaptured;
    bool Start(int width, int height, int fps);
    void Stop();
    CaptureInfo GetInfo();
}

public record CaptureInfo(int Width, int Height, int Fps, string Backend);
