using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using UniLinker.Core;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

/// <summary>
/// Hardware H.264 encoder via FFmpeg's h264_nvenc (NVIDIA NVENC).
/// The BGRA readback frame is fed to the encoder DIRECTLY when the codec
/// build supports it (nvenc converts ARGB→NV12 on the GPU — zero CPU
/// conversion); older builds fall back to the swscale BGRA→YUV420P path.
/// The encode stage itself costs near-zero CPU, which is what unlocks
/// 60fps on the 22fps CPU-encoder ceiling.
///
/// Same IEncoder contract as OpenH264Encoder: latest-wins frame slot,
/// background worker, periodic IDR via ForceKeyFrame.
/// Initialize() opens the codec synchronously so callers can fall back
/// to the CPU encoder when NVENC is unavailable (no NVIDIA GPU/driver).
/// </summary>
public unsafe class NvEncEncoder : IEncoder
{
    /// <summary>Constant-quality target (0-51, lower = better). 16 keeps
    /// desktop text pixel-transparent while static frames stay tiny; motion
    /// rides to the tier's maxrate cap (48Mbps on the HD tier).</summary>
    public const int CqLevel = 16;

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
    private long _swsTicks;
    private long _encTicks;
    private long _encTotalTicks;
    private long _auBytes;
    private int _loopCount;
    private int _configuredWidth;
    private int _configuredHeight;
    private int _bitrateKbps;
    private int _fps;
    private bool _initialized;

    private AVCodecContext* _codecCtx;
    private SwsContext* _swsCtx;
    private AVFrame* _inputFrame;
    private AVPacket* _pkt;
    private byte[]? _compactBuf;
    /// <summary>True when nvenc accepts BGRA directly (ARGB hardware
    /// conversion) — the sws BGRA→YUV420P CPU pass is skipped entirely.</summary>
    private bool _useBgraDirect;

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
        // AboveNormal: the encode worker IS the latency critical path — the
        // capture callback feeds it inline and the pacer waits on its output.
        // Normal priority exposed it to the same descheduling stretches the
        // pacer thread suffered (see DirectRtpSender ctor note).
        _worker = new Thread(EncodeLoop)
        {
            IsBackground = true,
            Name = "NvEncEncoder",
            Priority = ThreadPriority.AboveNormal,
        };
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
        // Pixel format: BGRA direct when this nvenc build supports it — the
        // ARGB hardware conversion kills the sws BGRA→YUV420P CPU pass
        // (1.3-2.1ms/frame plus a CPU contention source at 60fps). Detected
        // from the codec's supported-format list; yuv420p+sws stays as the
        // fallback so an unexpected build can never silently drop the
        // whole session to the CPU encoder.
        _useBgraDirect = NvEncSupportsBgra(codec);
        _codecCtx->pix_fmt = _useBgraDirect
            ? AVPixelFormat.AV_PIX_FMT_BGRA
            : AVPixelFormat.AV_PIX_FMT_YUV420P;
        _codecCtx->bit_rate = (long)_bitrateKbps * 1000;
        // Two-second GOP for screen content; no B-frames, no lookahead:
        // every extra frame of pipeline is latency on the mirrored link.
        _codecCtx->gop_size = _fps * 2;
        _codecCtx->max_b_frames = 0;
        // The tier bitrate is now a LINK CAP, not a quality target (vbr+cq
        // above decides spend). VBV bounds the per-frame burst: cap the
        // worst frame at tier/3 (~half a second of budget) — the packet
        // pacer then spreads even that over tens of ms.
        _codecCtx->rc_max_rate = _codecCtx->bit_rate;
        _codecCtx->rc_buffer_size = (int)(_codecCtx->bit_rate / 3);

        // Quality: preset p1 was the lowest-quality/fastest tier and showed
        // visible blocking on motion; p4 costs ~2-3ms more GPU time at this
        // resolution and restores most of it. High profile + CABAC save
        // ~10-15% bitrate at equal quality; spatial AQ spends bits where
        // the eye looks.
        // p7 = the "slowest/best compression" rung (the realtime analog of
        // the offline x264 preset-8 school): RTX headroom makes the extra
        // GPU time free, buying quality per bitrate at zero latency cost.
        av_opt_set(_codecCtx->priv_data, "preset", "p7", 0);
        av_opt_set(_codecCtx->priv_data, "tune", "ull", 0);
        av_opt_set(_codecCtx->priv_data, "profile", "high", 0);
        av_opt_set(_codecCtx->priv_data, "coder", "cabac", 0);
        av_opt_set(_codecCtx->priv_data, "spatial-aq", "1", 0);
        av_opt_set(_codecCtx->priv_data, "delay", "0", 0);
        av_opt_set(_codecCtx->priv_data, "zerolatency", "1", 0);
        // Intra-refresh replaces periodic IDRs with intra-coded strips that
        // sweep across frames — no 200-400KB IDR bursts (join/PLI spikes),
        // constant wire load, loss regions heal as the sweep passes. The
        // decoder-side impact: none (plain ISlices in P frames).
        av_opt_set(_codecCtx->priv_data, "intra_refresh", "1", 0);
        // 4 slices per frame: Moonlight's runtime rule for OLD Qualcomm
        // decoders (GLES < 3.1 → Adreno 330 on the SM-P605 hits it) — the
        // Venus decoder pipelines slices, so it can start decoding slice 1
        // while slices 2-4 are still arriving instead of waiting for the
        // whole frame bitstream. Costs a little compression efficiency.
        av_opt_set(_codecCtx->priv_data, "slices", "4", 0);

        // Rate control: quality-targeted VBR (the OBS "cq" mode) instead of
        // CBR. Measured: CBR PADDING padded static-desktop P frames to
        // ~130KB (16-64Mbps) regardless of content — flat regions soaked up
        // the budget as compression noise. With rc=vbr + cq=19 screen text
        // is visually transparent at 3-15KB/frame and motion spikes ride to
        // the maxrate cap (the RTX ring + pacer absorb the spread).
        av_opt_set(_codecCtx->priv_data, "rc", "vbr", 0);
        av_opt_set(_codecCtx->priv_data, "cq", CqLevel.ToString(), 0);

        Log.Info("[NvEnc] opening h264_nvenc codec context...");
        int rc = avcodec_open2(_codecCtx, codec, null);
        if (rc < 0)
        {
            Log.Error($"[NvEnc] avcodec_open2 failed rc={rc} (no NVIDIA GPU or driver too old?)");
            return false;
        }
        Log.Info("[NvEnc] codec context open");

        if (_useBgraDirect)
        {
            // Input frame wraps the PINNED capture buffer per encode (data
            // pointers set per frame) — zero conversion, zero extra copy.
            _inputFrame = av_frame_alloc();
            _inputFrame->width = _configuredWidth;
            _inputFrame->height = _configuredHeight;
            _inputFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        }
        else
        {
            // BGRA -> YUV420P conversion context (fallback codec builds).
            _swsCtx = sws_getContext(
                _configuredWidth, _configuredHeight, AVPixelFormat.AV_PIX_FMT_BGRA,
                _configuredWidth, _configuredHeight, AVPixelFormat.AV_PIX_FMT_YUV420P,
                (int)SwsFlags.SWS_BILINEAR, null, null, null);
            if (_swsCtx == null)
            {
                Log.Error("[NvEnc] sws_getContext failed");
                return false;
            }

            _inputFrame = av_frame_alloc();
            _inputFrame->width = _configuredWidth;
            _inputFrame->height = _configuredHeight;
            _inputFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            if (av_frame_get_buffer(_inputFrame, 0) < 0)
            {
                Log.Error("[NvEnc] av_frame_get_buffer failed");
                return false;
            }
        }

        _pkt = av_packet_alloc();
        Log.Info($"[NvEnc] initialized {_configuredWidth}x{_configuredHeight}@{_fps} CBR {_bitrateKbps}kbps fmt={(_useBgraDirect ? "bgra-direct" : "sws-yuv420p")} slices=4");
        return true;
    }

    /// <summary>Check the codec's supported pixel-format list for BGRA.
    /// Unknown/failed lookups conservatively return false (the sws fallback
    /// is always correct, only slower).</summary>
    private static unsafe bool NvEncSupportsBgra(AVCodec* codec)
    {
        try
        {
            void* cfg = null;
            int num = 0;
            if (avcodec_get_supported_config(null, codec,
                    AVCodecConfig.AV_CODEC_CONFIG_PIX_FORMAT, 0, &cfg, &num) < 0 || num <= 0)
            {
                return false;
            }
            var fmts = (AVPixelFormat*)cfg;
            for (int i = 0; i < num; i++)
            {
                if (fmts[i] == AVPixelFormat.AV_PIX_FMT_BGRA) return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
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

    public void ForceKeyFrame()
    {
        _forceIdr = true;
        // Wake the event-driven loop so a forced IDR fires even on a static
        // screen (the capture produces no frames to release the signal).
        try { _workSignal.Release(); } catch (SemaphoreFullException) { }
    }

    private void EncodeLoop()
    {
        // Event-driven latest-wins: encode the newest capture the moment it
        // arrives. The WGC capture throttle is the single fps stage - the old
        // deadline loop added a SECOND uncorrelated 1/fps wait of up to a
        // frame interval per frame (avg ~16-33ms of pure pacing wait at 30fps
        // tier). Static screens produce no capture frames - ForceKeyFrame
        // releases the signal so a forced IDR still fires on the cached frame.
        while (_running)
        {
            try
            {
                _workSignal.Wait();

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
                    var loopStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    EncodeOne(frame);
                    _encTotalTicks += System.Diagnostics.Stopwatch.GetTimestamp() - loopStart;
                    _loopCount++;
                }
                else if (_lastFrame != null && _forceIdr)
                {
                    // A forced IDR must go out even with no fresh capture.
                    long elapsedUs =
                        (System.Diagnostics.Stopwatch.GetTimestamp() - _lastFrameCachedAt)
                        * 1_000_000L / System.Diagnostics.Stopwatch.Frequency;
                    EncodeOne(_lastFrame with { TimestampUs = _lastFrame.TimestampUs + elapsedUs });
                    _forceIdr = false;
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
            if (!_initialized || _codecCtx == null || _inputFrame == null ||
                (!_useBgraDirect && _swsCtx == null)) return;

            // The source is read straight from the PINNED managed frame
            // buffer — no per-frame 4MB HGlobal alloc+copy (measured: this
            // was the 16fps bottleneck; pinned-direct restores the tier).
            var srcData = new byte*[4];
            var srcStride = new int[4];
            GCHandle pinned = default;
            try
            {
                if (frame.Pitch == frame.Width * 4)
                {
                    pinned = GCHandle.Alloc(frame.RawData, GCHandleType.Pinned);
                    srcData[0] = (byte*)pinned.AddrOfPinnedObject();
                    srcStride[0] = frame.Pitch;
                }
                else
                {
                    // Pitch-padded frame: compact into a reusable buffer.
                    int bgraSize = frame.Width * 4 * frame.Height;
                    if (_compactBuf == null || _compactBuf.Length < bgraSize)
                        _compactBuf = new byte[bgraSize];
                    int toCopy = Math.Min(frame.RawData.Length, bgraSize);
                    // Row-wise copy honoring the source pitch.
                    for (int y = 0; y < frame.Height; y++)
                        Buffer.BlockCopy(frame.RawData, y * frame.Pitch,
                            _compactBuf, y * frame.Width * 4, frame.Width * 4);
                    pinned = GCHandle.Alloc(_compactBuf, GCHandleType.Pinned);
                    srcData[0] = (byte*)pinned.AddrOfPinnedObject();
                    srcStride[0] = frame.Width * 4;
                    _ = toCopy;
                }

                if (_useBgraDirect)
                {
                    // BGRA direct: the input frame wraps the pinned capture
                    // buffer — nvenc does the ARGB→NV12 conversion on the
                    // GPU. No sws, no extra copy.
                    _inputFrame->data[0] = srcData[0];
                    _inputFrame->linesize[0] = srcStride[0];
                }
                else
                {
                    var swsStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    sws_scale(_swsCtx, srcData, srcStride, 0, frame.Height,
                        _inputFrame->data, _inputFrame->linesize);
                    _swsTicks += (System.Diagnostics.Stopwatch.GetTimestamp() - swsStart) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                }
            }
            finally
            {
                if (pinned.IsAllocated) pinned.Free();
            }

            _inputFrame->pts = frame.TimestampUs * _fps / 1_000_000;
            _inputFrame->pict_type = _forceIdr ? AVPictureType.AV_PICTURE_TYPE_I : AVPictureType.AV_PICTURE_TYPE_NONE;
            _forceIdr = false;

            var encStart = System.Diagnostics.Stopwatch.GetTimestamp();

            int sendRc = avcodec_send_frame(_codecCtx, _inputFrame);
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
                _auBytes += au.Length;
                if (_isKey) _keyFrames++;
                if (_emittedFrames % 60 == 1)
                {
                    var avgSws = _swsTicks / 60.0;
                    var avgEnc = _encTicks / 60.0;
                    var avgTotal = _loopCount > 0 ? _encTotalTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _loopCount : 0;
                    var avgAu = _auBytes / 60.0 / 1000.0;
                    Log.Info($"[NvEnc] emitted {_emittedFrames} frames (keys {_keyFrames}, last {au.Length}B key={isKey}) avgAu={avgAu:F1}KB ({avgAu * 60 * 8 / 1000:F1}Mbps) swsAvg={avgSws:F1}ms encAvg={avgEnc:F1}ms loops={_loopCount} fmt={(_useBgraDirect ? "bgra" : "yuv420p")}");
                    _swsTicks = 0;
                    _encTicks = 0;
                    _encTotalTicks = 0;
                    _auBytes = 0;
                    _loopCount = 0;
                }

                PacketEncoded?.Invoke(new EncodedPacket(au, frame.TimestampUs, isKey));
            }

            _encTicks += (System.Diagnostics.Stopwatch.GetTimestamp() - encStart) * 1000 / System.Diagnostics.Stopwatch.Frequency;
        }
    }

    public void Dispose()
    {
        _running = false;
        // Unbounded join: the worker checks _running every iteration and the
        // current EncodeOne holds _encoderGate — freeing the codec while the
        // worker is mid-encode was a use-after-free (coreclr 0xc0000005
        // crash, observed when the viewer disconnected mid-encode).
        _worker?.Join();
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
        if (_inputFrame != null)
        {
            var frame = _inputFrame;
            av_frame_free(&frame);
            _inputFrame = null;
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
