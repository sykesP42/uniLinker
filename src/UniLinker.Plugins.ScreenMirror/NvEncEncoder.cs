using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using UniLinker.Core;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

/// <summary>
/// Hardware H.264 encoder via FFmpeg's h264_nvenc (NVIDIA NVENC).
/// The BGRA readback frame is converted to YUV420P by swscale and handed
/// to the GPU encoder — the encode stage itself costs near-zero CPU,
/// which is what unlocks 60fps on the 22fps CPU-encoder ceiling.
///
/// Same IEncoder contract as OpenH264Encoder: latest-wins frame slot,
/// background worker, periodic IDR via ForceKeyFrame.
/// Initialize() opens the codec synchronously so callers can fall back
/// to the CPU encoder when NVENC is unavailable (no NVIDIA GPU/driver).
/// </summary>
public unsafe class NvEncEncoder : IEncoder
{
    private readonly object _slotGate = new();
    private CaptureFrame? _pendingFrame;
    private CaptureFrame? _lastFrame;
    private long _lastFrameCachedAt;
    private readonly object _encoderGate = new();
    private SemaphoreSlim _workSignal = new(0, 128);
    private volatile bool _running;
    private Thread? _worker;
    private volatile bool _forceIdr;
    private int _emittedFrames;
    private int _keyFrames;
    private bool _isKey;
    private int _configuredWidth;
    private int _configuredHeight;
    private int _bitrateKbps;
    private int _fps;
    private bool _initialized;

    private AVCodecContext* _codecCtx;
    private SwsContext* _swsCtx;
    private AVFrame* _yuvFrame;
    private AVPacket* _pkt;

    public event Action<EncodedPacket>? PacketEncoded;

    public bool Initialize(int width, int height, int fps, int bitrateKbps)
    {
        _configuredWidth = width;
        _configuredHeight = height;
        _fps = fps;
        _bitrateKbps = bitrateKbps;

        lock (_encoderGate)
        {
            if (!InitCodec())
            {
                FreeCodec();
                return false;
            }
        }

        _initialized = true;
        _running = true;
        _worker = new Thread(EncodeLoop) { IsBackground = true, Name = "NvEncEncoder" };
        _worker.Start();
        return true;
    }

    private bool InitCodec()
    {
        var codec = avcodec_find_encoder_by_name("h264_nvenc");
        if (codec == null)
        {
            Log.Error("[NvEnc] h264_nvenc encoder not found in FFmpeg build");
            return false;
        }

        _codecCtx = avcodec_alloc_context3(codec);
        if (_codecCtx == null) return false;

        _codecCtx->width = _configuredWidth;
        _codecCtx->height = _configuredHeight;
        _codecCtx->time_base = new AVRational { num = 1, den = _fps };
        _codecCtx->framerate = new AVRational { num = _fps, den = 1 };
        _codecCtx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
        _codecCtx->bit_rate = (long)_bitrateKbps * 1000;
        // Two-second GOP for screen content; no B-frames, no lookahead:
        // every extra frame of pipeline is latency on the mirrored link.
        _codecCtx->gop_size = _fps * 2;
        _codecCtx->max_b_frames = 0;

        av_opt_set(_codecCtx->priv_data, "preset", "p1", 0);
        av_opt_set(_codecCtx->priv_data, "tune", "ull", 0);
        av_opt_set(_codecCtx->priv_data, "rc", "cbr", 0);
        av_opt_set(_codecCtx->priv_data, "delay", "0", 0);
        av_opt_set(_codecCtx->priv_data, "zerolatency", "1", 0);

        Log.Info("[NvEnc] opening h264_nvenc codec context...");
        int rc = avcodec_open2(_codecCtx, codec, null);
        if (rc < 0)
        {
            Log.Error($"[NvEnc] avcodec_open2 failed rc={rc} (no NVIDIA GPU or driver too old?)");
            return false;
        }
        Log.Info("[NvEnc] codec context open");

        // BGRA -> YUV420P conversion context. NVENC accepts yuv420p system
        // memory frames; the GPU-D3D11 NV12 path is a later optimization.
        _swsCtx = sws_getContext(
            _configuredWidth, _configuredHeight, AVPixelFormat.AV_PIX_FMT_BGRA,
            _configuredWidth, _configuredHeight, AVPixelFormat.AV_PIX_FMT_YUV420P,
            (int)SwsFlags.SWS_BILINEAR, null, null, null);
        if (_swsCtx == null)
        {
            Log.Error("[NvEnc] sws_getContext failed");
            return false;
        }

        _yuvFrame = av_frame_alloc();
        _yuvFrame->width = _configuredWidth;
        _yuvFrame->height = _configuredHeight;
        _yuvFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
        if (av_frame_get_buffer(_yuvFrame, 0) < 0)
        {
            Log.Error("[NvEnc] av_frame_get_buffer failed");
            return false;
        }

        _pkt = av_packet_alloc();
        Log.Info($"[NvEnc] initialized {_configuredWidth}x{_configuredHeight}@{_fps} CBR {_bitrateKbps}kbps");
        return true;
    }

    public EncoderInfo GetInfo() => new("h264-nvenc", _bitrateKbps, IsHardware: true);

    public void Encode(CaptureFrame frame)
    {
        if (!_running) return;

        lock (_slotGate)
        {
            _pendingFrame = frame;
        }
        try
        {
            _workSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Worker still busy: _pendingFrame already holds the NEWEST frame.
        }
    }

    public void ForceKeyFrame() => _forceIdr = true;

    private void EncodeLoop()
    {
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / _fps);
        var nextDeadline = DateTime.UtcNow;

        while (_running)
        {
            try
            {
                var delay = nextDeadline - DateTime.UtcNow;
                if (delay > TimeSpan.Zero) Thread.Sleep(delay);
                nextDeadline += frameInterval;
                // Sleep overshoot recovery: never let the deadline run away.
                if (nextDeadline < DateTime.UtcNow - frameInterval)
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
                else if (_lastFrame != null && _forceIdr)
                {
                    // A forced IDR must go out even with no fresh capture.
                    long elapsedUs =
                        (System.Diagnostics.Stopwatch.GetTimestamp() - _lastFrameCachedAt)
                        * 1_000_000L / System.Diagnostics.Stopwatch.Frequency;
                    EncodeOne(_lastFrame with { TimestampUs = _lastFrame.TimestampUs + elapsedUs });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[NvEnc] encode loop error: {ex.Message}");
            }
        }
    }

    private void EncodeOne(CaptureFrame frame)
    {
        if (frame.RawData == null || frame.Width <= 0 || frame.Height <= 0) return;

        lock (_encoderGate)
        {
            if (!_initialized || _codecCtx == null || _swsCtx == null || _yuvFrame == null) return;

            int bgraSize = frame.Width * 4 * frame.Height;
            var bgraPtr = Marshal.AllocHGlobal(bgraSize);
            try
            {
                Marshal.Copy(frame.RawData, 0, bgraPtr, Math.Min(frame.RawData.Length, bgraSize));

                // sws_scale takes managed arrays (byte*[]/int[]), not raw
                // pointers — stackalloc'd buffers don't match the signature.
                var srcData = new byte*[4];
                var srcStride = new int[4];
                srcData[0] = (byte*)bgraPtr;
                srcStride[0] = frame.Width * 4;

                sws_scale(_swsCtx, srcData, srcStride, 0, frame.Height,
                    _yuvFrame->data, _yuvFrame->linesize);
            }
            finally
            {
                Marshal.FreeHGlobal(bgraPtr);
            }

            _yuvFrame->pts = frame.TimestampUs * _fps / 1_000_000;
            _yuvFrame->pict_type = _forceIdr ? AVPictureType.AV_PICTURE_TYPE_I : AVPictureType.AV_PICTURE_TYPE_NONE;
            _forceIdr = false;

            int sendRc = avcodec_send_frame(_codecCtx, _yuvFrame);
            if (sendRc < 0)
            {
                Log.Error($"[NvEnc] send_frame rc={sendRc}");
                return;
            }

            while (true)
            {
                int rc = avcodec_receive_packet(_codecCtx, _pkt);
                if (rc != 0 || _pkt == null) break;

                var au = new byte[_pkt->size];
                Marshal.Copy((nint)_pkt->data, au, 0, _pkt->size);
                bool isKey = (_pkt->flags & AV_PKT_FLAG_KEY) != 0;
                _isKey = isKey;

                _emittedFrames++;
                if (_isKey) _keyFrames++;
                if (_emittedFrames % 60 == 1)
                {
                    Log.Info($"[NvEnc] emitted {_emittedFrames} frames (keys {_keyFrames}, last {au.Length}B key={isKey} pts={_pkt->pts})");
                }

                PacketEncoded?.Invoke(new EncodedPacket(au, frame.TimestampUs, isKey));
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        _worker?.Join(3000);
        _worker = null;
        lock (_encoderGate)
        {
            FreeCodec();
        }
        PacketEncoded = null;
    }

    private void FreeCodec()
    {
        // Take addresses of locals — `&` on a class field (managed heap
        // pointer) is illegal without pinning (CS0212).
        if (_codecCtx != null)
        {
            var ctx = _codecCtx;
            avcodec_free_context(&ctx);
            _codecCtx = null;
        }
        if (_swsCtx != null)
        {
            sws_freeContext(_swsCtx);
            _swsCtx = null;
        }
        if (_yuvFrame != null)
        {
            var frame = _yuvFrame;
            av_frame_free(&frame);
            _yuvFrame = null;
        }
        if (_pkt != null)
        {
            var pkt = _pkt;
            av_packet_free(&pkt);
            _pkt = null;
        }
        _initialized = false;
    }
}
