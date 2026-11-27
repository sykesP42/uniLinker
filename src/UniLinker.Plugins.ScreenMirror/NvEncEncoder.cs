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
    /// <summary>Cancelled by Dispose — the worker's Wait must be
    /// interruptible: after _running=false the capture's Encode() returns
    /// WITHOUT releasing, so a worker sleeping in Wait() would hang the
    /// unbounded Dispose Join forever (the 03:06 watchdog-release hang).</summary>
    private readonly CancellationTokenSource _disposeCts = new();
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
    // ── Zero-copy D3D11 path (M7 round 4) ──
    private nint _sharedDevicePtr;
    private nint _sharedContextPtr;
    private object? _gpuGate;
    private bool _useHwFrames;
    private AVBufferRef* _hwDeviceRef;
    private AVBufferRef* _hwFramesRef;
    private AVFrame* _hwFrame;
    /// <summary>Cached wrapper over FFmpeg's pooled TEXTURE ARRAY (the
    /// d3d11 hwframes convention: frame->data[0] = array texture,
    /// data[1] = slice index). AddRef'd once — the pointer is borrowed
    /// from FFmpeg's pool, so the wrapper's Release is balanced by ours.</summary>
    private Vortice.Direct3D11.ID3D11Texture2D? _pooledArray;
    /// <summary>AddRef'd wrapper over the capture's shared immediate
    /// context — every CopySubresourceRegion runs under the shared GPU
    /// gate (the capture's VP blit takes the same gate).</summary>
    private Vortice.Direct3D11.ID3D11DeviceContext? _borrowedCtx;

    public event Action<EncodedPacket>? PacketEncoded;

    public bool Initialize(int width, int height, int fps, int bitrateKbps,
        nint sharedDevicePtr = 0, nint sharedContextPtr = 0, object? gpuGate = null)
    {
        _configuredWidth = width;
        _configuredHeight = height;
        _fps = fps;
        _bitrateKbps = bitrateKbps;
        _sharedDevicePtr = sharedDevicePtr;
        _sharedContextPtr = sharedContextPtr;
        _gpuGate = gpuGate;

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

    /// <summary>True when the zero-copy D3D11 path opened: the caller may
    /// then switch the capture to GPU delivery (CaptureFrame.D3dTexture).</summary>
    public bool UsesGpuFrames => _useHwFrames;

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
        // Pixel format: zero-copy D3D11 when a shared capture device was
        // provided AND the codec accepts D3D11 frames — the encoder then
        // consumes GPU-resident textures (no readback, no upload). Falls
        // back to BGRA-direct system memory (or sws yuv420p) so an
        // unexpected build can never silently drop the session to the CPU
        // encoder.
        var d3d11Supported = NvEncSupportsPixelFormat(codec, AVPixelFormat.AV_PIX_FMT_D3D11);
        Log.Info($"[NvEnc] hw probe: devPtr={_sharedDevicePtr:X} d3d11InPixfmts={d3d11Supported}");
        _useHwFrames = _sharedDevicePtr != 0 && d3d11Supported;
        _useBgraDirect = false;
        if (_useHwFrames)
        {
            if (!InitHwFrames()) return false;
            _codecCtx->pix_fmt = AVPixelFormat.AV_PIX_FMT_D3D11;
        }
        else
        {
            // BGRA direct when this nvenc build supports it — the ARGB
            // hardware conversion kills the sws BGRA→YUV420P CPU pass
            // (1.3-2.1ms/frame plus a CPU contention source at 60fps).
            _useBgraDirect = NvEncSupportsBgra(codec);
            _codecCtx->pix_fmt = _useBgraDirect
                ? AVPixelFormat.AV_PIX_FMT_BGRA
                : AVPixelFormat.AV_PIX_FMT_YUV420P;
        }
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

        if (_useHwFrames)
        {
            // Input frame receives pooled D3D11 array-texture slices from
            // av_hwframe_get_buffer — GPU-resident, no CPU touch.
            _hwFrame = av_frame_alloc();
            _hwFrame->width = _configuredWidth;
            _hwFrame->height = _configuredHeight;
            _hwFrame->format = (int)AVPixelFormat.AV_PIX_FMT_D3D11;
        }
        else if (_useBgraDirect)
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
        Log.Info($"[NvEnc] initialized {_configuredWidth}x{_configuredHeight}@{_fps} CBR {_bitrateKbps}kbps fmt={(_useHwFrames ? "d3d11-zero-copy" : _useBgraDirect ? "bgra-direct" : "sws-yuv420p")} slices=4");
        return true;
    }

    /// <summary>Zero-copy hwframes setup: wrap the capture's D3D11 device in
    /// an FFmpeg hwdevice (pre-filled AVD3D11VADeviceContext — a NEW device
    /// would break same-device CopySubresourceRegion), build the pooled
    /// texture-array frames context, and attach both to the codec context.
    /// Returns false on any failure (the caller falls back to the system-
    /// memory path). FFmpeg holds its own refs on the borrowed pointers.</summary>
    private unsafe bool InitHwFrames()
    {
        try
        {
            _hwDeviceRef = av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
            if (_hwDeviceRef == null) return false;
            var devCtxStruct = (AVHWDeviceContext*)_hwDeviceRef->data;
            var d3d11 = (AVD3D11VADeviceContext*)devCtxStruct->hwctx;
            d3d11->device = (FFmpeg.AutoGen.ID3D11Device*)_sharedDevicePtr;
            d3d11->device_context = (FFmpeg.AutoGen.ID3D11DeviceContext*)_sharedContextPtr;
            if (av_hwdevice_ctx_init(_hwDeviceRef) < 0)
            {
                Log.Error("[NvEnc] av_hwdevice_ctx_init(d3d11va) failed");
                return false;
            }

            _hwFramesRef = av_hwframe_ctx_alloc(_hwDeviceRef);
            if (_hwFramesRef == null) return false;
            var frames = (AVHWFramesContext*)_hwFramesRef->data;
            frames->format = AVPixelFormat.AV_PIX_FMT_D3D11;
            frames->sw_format = AVPixelFormat.AV_PIX_FMT_BGRA;
            frames->width = _configuredWidth;
            frames->height = _configuredHeight;
            frames->initial_pool_size = 8;
            // The capture VP-blits into pooled slices — DECODER-bound default
            // textures cannot take VideoProcessorOutputViews.
            var framesD3d11 = (AVD3D11VAFramesContext*)frames->hwctx;
            framesD3d11->BindFlags = (uint)Vortice.Direct3D11.BindFlags.RenderTarget;
            if (av_hwframe_ctx_init(_hwFramesRef) < 0)
            {
                Log.Error("[NvEnc] av_hwframe_ctx_init failed");
                return false;
            }

            _codecCtx->hw_device_ctx = av_buffer_ref(_hwDeviceRef);
            _codecCtx->hw_frames_ctx = av_buffer_ref(_hwFramesRef);
            // Borrow the shared immediate context (AddRef so the wrapper's
            // eventual Release cannot destroy the capture-owned object).
            Marshal.AddRef(_sharedContextPtr);
            _borrowedCtx = new Vortice.Direct3D11.ID3D11DeviceContext(_sharedContextPtr);
            Log.Info("[NvEnc] zero-copy d3d11 hwframes ready (shared capture device)");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[NvEnc] hwframes init failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Check the codec's supported pixel-format list for BGRA.
    /// Unknown/failed lookups conservatively return false (the sws fallback
    /// is always correct, only slower).</summary>
    private static unsafe bool NvEncSupportsBgra(AVCodec* codec) =>
        NvEncSupportsPixelFormat(codec, AVPixelFormat.AV_PIX_FMT_BGRA);

    private static unsafe bool NvEncSupportsPixelFormat(AVCodec* codec, AVPixelFormat fmt)
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
                if (fmts[i] == fmt) return true;
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
                _workSignal.Wait(_disposeCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            try
            {
                // (original loop body continues below)

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
        Log.Info("[NvEnc] worker loop exited");
    }

    private void EncodeOne(CaptureFrame frame)
    {
        if (!_useHwFrames && frame.RawData == null) return;
        if (frame.Width <= 0 || frame.Height <= 0) return;

        lock (_encoderGate)
        {
            if (!_initialized || _codecCtx == null) return;

            if (_useHwFrames)
            {
                EncodeOneHw(frame);
                return;
            }

            if (_inputFrame == null || (!_useBgraDirect && _swsCtx == null)) return;

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

    /// <summary>Zero-copy encode: pull a pooled D3D11 array-texture slice,
    /// CopySubresourceRegion the capture ring slot's frame into it under the
    /// shared GPU gate (same gate as the capture VP blit — the immediate
    /// context is not thread-safe), and hand the GPU-resident frame to
    /// nvenc. No CPU readback, no upload — the frame never leaves the GPU.</summary>
    private int dbgHwFrames;

    private unsafe void EncodeOneHw(CaptureFrame frame)
    {
        if (_hwFrame == null || _hwFramesRef == null || _borrowedCtx == null ||
            frame.D3dTexture == nint.Zero || _gpuGate == null) return;

        if (av_hwframe_get_buffer(_hwFramesRef, _hwFrame, 0) < 0)
        {
            // Pool exhausted (encoder backed up): latest-wins drops this frame.
            return;
        }

        // d3d11 hwframes convention: data[0] = pooled texture array,
        // data[1] = slice index (AVD3D11FrameDescriptor).
        var pooledPtr = (nint)_hwFrame->data[0];
        var sliceIdx = (uint)(nuint)_hwFrame->data[1];
        if (dbgHwFrames < 3)
        {
            dbgHwFrames++;
            Log.Info($"[NvEnc] hw#{dbgHwFrames}: pooled={pooledPtr:X} slice={sliceIdx} src={frame.D3dTexture:X}");
        }
        if (_pooledArray == null || _pooledArray.NativePointer != pooledPtr)
        {
            // Cache the wrapper per returned texture pointer: FFmpeg's d3d11
            // pool is normally ONE texture2D array (data[0] constant), but a
            // fallback build may hand out separate textures — a stale
            // wrapper here AVed in CopySubresourceRegion.
            _pooledArray?.Dispose();
            Marshal.AddRef(pooledPtr);
            _pooledArray = new Vortice.Direct3D11.ID3D11Texture2D(pooledPtr);
        }

        // The source ring-slot texture pointer is likewise borrowed from the
        // capture — AddRef before wrapping, Release after the copy.
        Marshal.AddRef(frame.D3dTexture);
        var srcTexture = new Vortice.Direct3D11.ID3D11Texture2D(frame.D3dTexture);
        try
        {
            lock (_gpuGate)
            {
                _borrowedCtx.CopySubresourceRegion(_pooledArray, sliceIdx,
                    0, 0, 0, srcTexture, 0, null);
            }
        }
        finally
        {
            srcTexture.Dispose();
            Marshal.Release(frame.D3dTexture);
        }

        _hwFrame->pts = frame.TimestampUs * _fps / 1_000_000;
        _hwFrame->pict_type = _forceIdr ? AVPictureType.AV_PICTURE_TYPE_I : AVPictureType.AV_PICTURE_TYPE_NONE;
        _forceIdr = false;

        var encStart = System.Diagnostics.Stopwatch.GetTimestamp();

        int sendRc = avcodec_send_frame(_codecCtx, _hwFrame);
        // Return the pool slice: the codec holds its own ref after send.
        av_frame_unref(_hwFrame);
        if (sendRc < 0)
        {
            Log.Error($"[NvEnc] send_frame(d3d11) rc={sendRc}");
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
                var avgEnc = _encTicks / 60.0;
                var avgAu = _auBytes / 60.0 / 1000.0;
                Log.Info($"[NvEnc] emitted {_emittedFrames} frames (keys {_keyFrames}, last {au.Length}B key={isKey}) avgAu={avgAu:F1}KB ({avgAu * 60 * 8 / 1000:F1}Mbps) encAvg={avgEnc:F1}ms loops={_loopCount} fmt=d3d11-zero-copy");
                _encTicks = 0;
                _encTotalTicks = 0;
                _auBytes = 0;
                _loopCount = 0;
            }

            PacketEncoded?.Invoke(new EncodedPacket(au, frame.TimestampUs, isKey));
        }

        _encTicks += (System.Diagnostics.Stopwatch.GetTimestamp() - encStart) * 1000 / System.Diagnostics.Stopwatch.Frequency;
    }

    public void Dispose()
    {
        _running = false;
        try { _disposeCts.Cancel(); } catch { }
        Log.Info("[NvEnc] dispose: joining worker");
        // Unbounded join: the worker checks _running every iteration and the
        // current EncodeOne holds _encoderGate — freeing the codec while the
        // worker is mid-encode was a use-after-free (coreclr 0xc0000005
        // crash, observed when the viewer disconnected mid-encode).
        _worker?.Join();
        Log.Info("[NvEnc] dispose: worker joined");
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
        if (_hwFramesRef != null)
        {
            var r = _hwFramesRef;
            av_buffer_unref(&r);
            _hwFramesRef = null;
        }
        if (_hwDeviceRef != null)
        {
            var r = _hwDeviceRef;
            av_buffer_unref(&r);
            _hwDeviceRef = null;
        }
        if (_hwFrame != null)
        {
            var f = _hwFrame;
            av_frame_free(&f);
            _hwFrame = null;
        }
        _pooledArray?.Dispose();
        _pooledArray = null;
        _borrowedCtx?.Dispose();
        _borrowedCtx = null;
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
