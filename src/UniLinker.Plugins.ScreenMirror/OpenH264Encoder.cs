using H264Sharp;
using UniLinker.Core;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

/// <summary>
/// H.264 software encoder wrapping Cisco OpenH264 (via H264Sharp).
/// Emits one EncodedPacket per complete Annex-B access unit, ready for
/// SIPSorcery's SendVideo (which parses start-code delimited NALs itself).
///
/// Runs on a dedicated worker thread with a "latest frame wins" slot so a
/// slow encode never builds a backlog: frames are throttled to the target
/// fps and the previous frame is re-encoded when capture is idle (static
/// screen) to keep the RTP stream alive.
/// </summary>
public class OpenH264Encoder : IEncoder
{
    private H264Encoder? _encoder;
    private int _configuredWidth;
    private int _configuredHeight;
    private int _fps = 30;
    private int _bitrateKbps = 8000;

    private readonly object _slotGate = new();
    private CaptureFrame? _pendingFrame;
    private CaptureFrame? _lastFrame;
    private readonly SemaphoreSlim _workSignal = new(0, 1);
    private Thread? _worker;
    private volatile bool _running;

    public event Action<EncodedPacket>? PacketEncoded;
    private int _emittedFrames;

    public bool Initialize(int width, int height, int fps, int bitrateKbps)
    {
        // Actual capture dimensions are only known when the first frame
        // arrives; the real encoder is created lazily in the worker.
        _configuredWidth = width;
        _configuredHeight = height;
        _fps = Math.Clamp(fps, 1, 120);
        _bitrateKbps = Math.Clamp(bitrateKbps, 500, 80000);

        _running = true;
        _worker = new Thread(EncodeLoop)
        {
            IsBackground = true,
            Name = "OpenH264-encode",
        };
        _worker.Start();
        return true;
    }

    public void Encode(CaptureFrame frame)
    {
        if (!_running) return;

        lock (_slotGate)
        {
            _pendingFrame = frame;
        }
        _workSignal.Release();
    }

    public void ForceKeyFrame()
    {
        _encoder?.ForceIntraFrame();
    }

    private async void EncodeLoop()
    {
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / _fps);
        var nextDeadline = DateTime.UtcNow;

        while (_running)
        {
            try
            {
                var delay = nextDeadline - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay);
                nextDeadline = DateTime.UtcNow + frameInterval;

                CaptureFrame? frame;
                lock (_slotGate)
                {
                    frame = _pendingFrame;
                    _pendingFrame = null;
                }
                if (frame == null)
                {
                    // Capture is idle (static screen): re-emit the last frame
                    // as a fresh P-frame so the stream does not stall.
                    frame = _lastFrame;
                    if (frame == null) continue;
                }
                _lastFrame = frame;

                EncodeOne(frame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenH264] encode error: {ex.Message}");
            }
        }
    }

    private void EncodeOne(CaptureFrame frame)
    {
        if (frame.RawData == null || frame.Width <= 0 || frame.Height <= 0) return;

        int width = frame.Width;
        int height = frame.Height;

        if (_encoder == null || _configuredWidth != width || _configuredHeight != height)
        {
            _encoder?.Dispose();
            _encoder = new H264Encoder();
            int rc = _encoder.Initialize(
                width, height, _bitrateKbps * 1000, _fps, ConfigType.ScreenCaptureBasic);
            if (rc != 0)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenH264] init failed rc={rc}");
                _encoder.Dispose();
                _encoder = null;
                return;
            }
            _configuredWidth = width;
            _configuredHeight = height;
            System.Diagnostics.Debug.WriteLine(
                $"[OpenH264] encoder ready {width}x{height} @{_fps}fps {_bitrateKbps}kbps");
        }

        using var image = new RgbImage(ImageFormat.Bgra, width, height, frame.Pitch, frame.RawData);
        bool ok = _encoder.Encode(image, out EncodedData[] encoded);
        if (!ok || encoded.Length == 0) return;

        byte[] accessUnit = encoded.GetAllBytes();
        bool isKeyFrame = encoded[0].FrameType == FrameType.IDR;
        _emittedFrames++;
        if (_emittedFrames % 60 == 1)
        {
            Log.Info($"OpenH264 emitted {_emittedFrames} frames (last {accessUnit.Length}B key={isKeyFrame})");
        }
        PacketEncoded?.Invoke(new EncodedPacket(accessUnit, frame.TimestampUs, isKeyFrame));
    }

    public EncoderInfo GetInfo() => new("h264_openh264", _bitrateKbps, IsHardware: false);

    public void Dispose()
    {
        _running = false;
        _workSignal.Release();
        _worker?.Join(TimeSpan.FromSeconds(2));
        _encoder?.Dispose();
        _encoder = null;
        _workSignal.Dispose();
    }
}
