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

    /// <summary>Stopwatch timestamp (not a wall clock) of when <see cref="_lastFrame"/>
    /// was captured. Used to advance the timestamp of idle re-emits so the RTP
    /// 90 kHz clock keeps tracking real time instead of freezing.</summary>
    private long _lastFrameCachedAt;

    /// <summary>Guards <see cref="_encoder"/> between the encode worker and
    /// <see cref="ForceKeyFrame"/>. Without it, a key-frame request racing an
    /// in-flight encode or a resolution change can hit a disposed encoder.</summary>
    private readonly object _encoderGate = new();
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
        lock (_encoderGate)
        {
            _encoder?.ForceIntraFrame();
        }
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

                if (frame != null)
                {
                    _lastFrame = frame;
                    _lastFrameCachedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                    EncodeOne(frame);
                }
                else if (_lastFrame != null)
                {
                    // Capture is idle (static screen): re-encode the cached frame
                    // as a fresh P-frame so the stream keeps its cadence. The
                    // timestamp MUST advance into the future -- re-using the stale
                    // capture timestamp would freeze the RTP 90 kHz clock, and the
                    // next real frame would then be given an absurdly long
                    // duration, showing up as a multi-second freeze at the receiver.
                    long elapsedUs =
                        (System.Diagnostics.Stopwatch.GetTimestamp() - _lastFrameCachedAt)
                        * 1_000_000L / System.Diagnostics.Stopwatch.Frequency;
                    EncodeOne(_lastFrame with { TimestampUs = _lastFrame.TimestampUs + elapsedUs });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenH264] encode error: {ex.Message}");
            }
        }
    }

    private void EncodeOne(CaptureFrame frame)
    {
        // Serialize against ForceKeyFrame/Dispose so a key-frame request or a
        // shutdown can never touch a disposed or mid-reconfigure encoder.
        lock (_encoderGate)
        {
            EncodeOneCore(frame);
        }
    }

    private void EncodeOneCore(CaptureFrame frame)
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
        lock (_encoderGate)
        {
            _encoder?.Dispose();
            _encoder = null;
        }
        _workSignal.Dispose();
    }
}
