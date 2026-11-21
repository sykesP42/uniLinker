using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using static FFmpeg.AutoGen.ffmpeg;
using UniLinker.Core; // Log (Sdk-internal, UniLinker.Core namespace)
using UniLinker.Plugin.Sdk;
namespace UniLinker.Plugins.ScreenMirror;

/// <summary>
/// Tile-transport encoder (M6): the remote-desktop-family pipeline. Splits
/// each captured BGRA frame into a fixed tile grid, hash-diffs tiles against
/// the previous frame, and JPEG-encodes ONLY the changed tiles with FFmpeg's
/// mjpeg encoder (per-tile independence = loss never cascades; a missing
/// tile costs one grid cell, healed by the next frame that touches it).
///
/// Wire payload per emitted EncodedPacket (one RTP packet, PT 97):
///   [tileIdx u16][tileCount u16][chunkIdx u8][chunkCount u8][flags u8][jpeg...]
/// flags bit0 = last packet of the frame (the RTP marker source); the tile
/// sender reads it. A tile larger than the chunk budget is split across
/// chunks; all packets of one frame share TimestampUs (= frame id).
///
/// Every packet is a keyframe by construction (MJPEG is intra-only);
/// EncodedPacket.IsKeyFrame means "full refresh frame" (all tiles sent) and
/// doubles as the pacer's queue-clear signal.
/// </summary>
public unsafe class TileEncoder : IEncoder
{

    /// <summary>Tile grid cell width (multiple of 16 for YUVJ420P MCU).
    /// 640x400 (was 320x200): the per-tile gate/blit/present overhead —
    /// not the pixel decode — capped the tablet at ~200 tiles/s (12fps of
    /// a 16-tile frame); quartering the tile count at the same pixel
    /// throughput lifts the render floor ~4x (2026-11-21).</summary>
    public const int TileW = 640;
    /// <summary>Tile grid cell height.</summary>
    public const int TileH = 400;
    /// <summary>RTP payload budget minus the 6-byte tile header and the 1-byte
    /// flags trailer: keeps the wire packet ≤ MTU with the 12B RTP header +
    /// 10B SRTP tag. Mirrors DirectRtpSender.MaxPayload (1400) — keep in sync.</summary>
    public const int ChunkBudget = 1400 - 7;
    public const byte FlagLastOfFrame = 0x01;

    // Quality ladder (commercial-remote-software degradation model: shed
    // QUALITY under link pressure, not frames — blur is recoverable in a
    // second, stutter is not). Downgrades instantly on backlog, upgrades
    // only after a sustained idle window so the level never flaps.
    private int[] QualityLevels = [82, 64, 48];
    private const int UpgradeIdleFrames = 45; // ~0.75s at 60fps
    private int _qualityLevel;                // index into QualityLevels
    private int _idleFrames;
    private int _skipCounter;                 // bottom-rung 30fps shaping

    private readonly object _slotGate = new();
    private CaptureFrame? _pendingFrame;
    private long _pendingTsUs;
    private bool _pendingIsRefresh;
    private SemaphoreSlim _workSignal = new(0, 32);
    private volatile bool _running;
    private Thread? _worker;
    private volatile bool _forceFullRefresh;

    private int _gridCols;
    private int _gridRows;
    private int _fps;
    private byte[]? _prevRaw; // previous frame BGRA (fresh per frame from WGC)
    private int _frameCounter;
    private long _lastFullRefreshFrame;

    private SwsContext* _sws;
    private AVFrame* _yuvFrame;
    private readonly Dictionary<(int, int, int), IntPtr> _encoders = new();
    private readonly Dictionary<(int, int, int), IntPtr> _tileFrames = new();
    private readonly List<byte[]> _emit = new();

    private long _swsTicks;
    private long _diffTicks;
    private long _encTicks;
    private long _frameBytes;
    private int _emittedFrames;
    private int _tilesSent;
    private int _loopCount;

    public event Action<EncodedPacket>? PacketEncoded;

    /// <summary>Pacer-queue pressure provider (frames queued ahead of the
    /// pacer, 0-4). Wired by the plugin to the channel's TileBacklog; null =
    /// no signal (quality stays at the top rung).</summary>
    public Func<int>? TileBacklogProvider { get; set; }

    public bool Initialize(int width, int height, int fps, int bitrateKbps)
    {
        // Quality ladder base from the decode preference (82 = 极速, 92 =
        // 高画质 via TileQualityBase); rungs keep the -18/-16 spacing.
        var baseQ = DirectRtpTransport.TileQualityBase;
        QualityLevels = [baseQ, Math.Max(32, baseQ - 18), Math.Max(24, baseQ - 34)];
        _fps = fps;
        _gridCols = (width + TileW - 1) / TileW;
        _gridRows = (height + TileH - 1) / TileH;

        // Probe mjpeg availability up-front (same pattern as NvEncEncoder).
        lock (_encoders)
        {
            if (!GetEncoder(TileW, TileH, 0)) return false;
        }

        _running = true;
        _forceFullRefresh = true; // first frame sends every tile
        _worker = new Thread(EncodeLoop) { IsBackground = true, Name = "TileEncoder" };
        _worker.Start();
        Log.Info($"[Tile] initialized {width}x{height} grid {_gridCols}x{_gridRows} ({_gridCols * _gridRows} tiles) jpeg q ladder [{string.Join('/', QualityLevels)}]");
        return true;
    }

    public EncoderInfo GetInfo() => new("tile-mjpeg", 0, IsHardware: false);

    public void Encode(CaptureFrame frame)
    {
        if (!_running) return;
        lock (_slotGate)
        {
            _pendingFrame = frame;
            _pendingTsUs = frame.TimestampUs;
        }
        try { _workSignal.Release(); } catch (SemaphoreFullException) { }
    }

    public void ForceKeyFrame() => _forceFullRefresh = true;

    private void EncodeLoop()
    {
        while (_running)
        {
            _workSignal.Wait();
            if (!_running) break;
            try
            {
                CaptureFrame? frame;
                lock (_slotGate)
                {
                    frame = _pendingFrame;
                    _pendingFrame = null;
                }
                if (frame?.RawData == null || frame.Width <= 0) continue;

                EncodeOne(frame, _pendingTsUs);
            }
            catch (Exception ex)
            {
                Log.Error($"[Tile] encode error: {ex.Message}");
            }
        }
    }

    private void EncodeOne(CaptureFrame frame, long tsUs)
    {
        // Bottom-rung rate shaping: at q48 the quality lever alone can't fit
        // heavy content into the WiFi budget, so every other frame is skipped
        // (30fps). The skipped frame does NOT update _prevRaw — its changes
        // merge into the next diff (superset of tiles, never missing ones).
        // Full-refresh frames (PLI recovery) are never skipped.
        if (_qualityLevel == QualityLevels.Length - 1 && !_forceFullRefresh)
        {
            if (++_skipCounter % 2 == 0) return;
        }
        else
        {
            _skipCounter = 0;
        }

        var swsStart = System.Diagnostics.Stopwatch.GetTimestamp();
        lock (_encoders)
        {
            if (_sws == null || _yuvFrame == null || _yuvFrame->width != frame.Width)
            {
                RebuildScale(frame.Width, frame.Height);
            }

            // BGRA -> YUVJ420P (full frame, one pass).
            var src = PinSource(frame);
            try
            {
                var srcData = new byte*[4];
                var srcStride = new int[4];
                srcData[0] = (byte*)src.AddrOfPinnedObject();
                srcStride[0] = frame.Pitch;
                sws_scale(_sws, srcData, srcStride, 0, frame.Height,
                    _yuvFrame->data, _yuvFrame->linesize);
            }
            finally
            {
                if (src.IsAllocated) src.Free();
            }
            _swsTicks += (System.Diagnostics.Stopwatch.GetTimestamp() - swsStart) * 1000 / System.Diagnostics.Stopwatch.Frequency;
        }

        // ── Tile diff (memcmp vs the previous frame's BGRA — CRT SIMD) ──
        var diffStart = System.Diagnostics.Stopwatch.GetTimestamp();
        bool fullRefresh = _forceFullRefresh ||
                           _prevRaw == null ||
                           _prevRaw.Length != frame.RawData!.Length ||
                           _frameCounter - _lastFullRefreshFrame >= Math.Max(_fps, _fps * 2);
        var changed = new List<int>();
        if (fullRefresh)
        {
            for (int i = 0; i < _gridCols * _gridRows; i++) changed.Add(i);
            _forceFullRefresh = false;
            _lastFullRefreshFrame = _frameCounter;
        }
        else
        {
            // fixed() is a transient pin (cheap — no GC Pinned bookkeeping,
            // unlike GCHandle.Alloc which measured 20ms+ per frame here).
            fixed (byte* pPrev = _prevRaw, pCur = frame.RawData)
            {
                for (int ty = 0; ty < _gridRows; ty++)
                {
                    for (int tx = 0; tx < _gridCols; tx++)
                    {
                        int idx = ty * _gridCols + tx;
                        if (TileDiffers(pPrev, pCur, frame.Pitch, tx, ty,
                                TileWidth(frame.Width, tx), TileHeight(frame.Height, ty)))
                            changed.Add(idx);
                    }
                }
            }
            if (changed.Count == 0)
            {
                _frameCounter++;
                _prevRaw = frame.RawData;
                return; // static frame: nothing on the wire at all
            }
        }
        _diffTicks += (System.Diagnostics.Stopwatch.GetTimestamp() - diffStart) * 1000 / System.Diagnostics.Stopwatch.Frequency;
        _prevRaw = frame.RawData;

        // ── Encode changed tiles ──
        UpdateQualityLadder();
        var encStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var framePackets = new List<byte[]>();
        lock (_encoders)
        {
            var dumped = false;
            foreach (int idx in changed)
            {
                int tx = idx % _gridCols;
                int ty = idx / _gridCols;
                int tw = TileWidth(frame.Width, tx);
                int th = TileHeight(frame.Height, ty);
                var jpeg = EncodeTile(frame.Width, frame.Height, tx, ty, tw, th, _qualityLevel);
                if (jpeg == null || jpeg.Length == 0) continue;
                // DEBUG: dump the first full-refresh tile once for content
                // verification (black tiles here = PC-side slicing bug).
                if (fullRefresh && !dumped && changed.Count > 1)
                {
                    var dumpPath = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(), "tile_dump.jpg");
                    System.IO.File.WriteAllBytes(dumpPath, jpeg);
                    Log.Info($"[Tile] dumped full-refresh tile idx={idx} ({tw}x{th}, {jpeg.Length}B) -> {dumpPath}");
                    dumped = true;
                }
                ChunkTile(framePackets, idx, _gridCols * _gridRows, jpeg);
            }
        }
        _encTicks += (System.Diagnostics.Stopwatch.GetTimestamp() - encStart) * 1000 / System.Diagnostics.Stopwatch.Frequency;

        // ── Emit: one EncodedPacket per FRAME (all wire packets
        // concatenated, each prefixed by its u16 BE length). The sender
        // splits and paces; the queue drops whole stale frames instead of
        // tearing a full refresh into holes (which painted black tiles).
        if (framePackets.Count > 0)
        {
            var last = framePackets[^1];
            last[^1] |= FlagLastOfFrame; // flags byte is the payload's last byte
            var blob = new byte[framePackets.Sum(p => p.Length) + framePackets.Count * 2];
            int pos = 0;
            foreach (var pkt in framePackets)
            {
                blob[pos] = (byte)(pkt.Length >> 8);
                blob[pos + 1] = (byte)pkt.Length;
                Buffer.BlockCopy(pkt, 0, blob, pos + 2, pkt.Length);
                pos += 2 + pkt.Length;
            }
            PacketEncoded?.Invoke(new EncodedPacket(blob, tsUs, fullRefresh));
        }

        _frameCounter++;
        _emittedFrames++;
        _tilesSent += changed.Count;
        _frameBytes += framePackets.Sum(p => p.Length);
        _loopCount++;
        if (_emittedFrames % 60 == 1)
        {
            var avgSws = _loopCount > 0 ? _swsTicks / (double)_loopCount : 0;
            var avgDiff = _loopCount > 0 ? _diffTicks / (double)_loopCount : 0;
            var avgEnc = _loopCount > 0 ? _encTicks / (double)_loopCount : 0;
            var mbps = _loopCount > 0 ? _frameBytes * 8.0 / 1000 / 1000 * 30 : 0;
            Log.Info($"[Tile] emitted {_emittedFrames} frames q={QualityLevels[_qualityLevel]} (tiles {_tilesSent}/{_gridCols * _gridRows}, last {framePackets.Count} pkts) avgAu={_frameBytes / 60.0 / 1000:F1}KB ({mbps:F1}Mbps) swsAvg={avgSws:F1}ms diffAvg={avgDiff:F1}ms encAvg={avgEnc:F1}ms loops={_loopCount}");
            _swsTicks = 0; _diffTicks = 0; _encTicks = 0; _frameBytes = 0; _loopCount = 0; _tilesSent = 0;
        }
    }

    /// <summary>Pin frame.RawData honoring pitch (compact pitch-padded rows).</summary>
    private GCHandle PinSource(CaptureFrame frame)
    {
        if (frame.Pitch == frame.Width * 4)
        {
            return GCHandle.Alloc(frame.RawData, GCHandleType.Pinned);
        }
        int size = frame.Width * 4 * frame.Height;
        var buf = new byte[size];
        for (int y = 0; y < frame.Height; y++)
            Buffer.BlockCopy(frame.RawData, y * frame.Pitch, buf, y * frame.Width * 4, frame.Width * 4);
        return GCHandle.Alloc(buf, GCHandleType.Pinned);
    }

    /// <summary>Row-wise ulong-stepped compare of one tile between two BGRA
    /// frames (identical pitch assumed — same capture config). 8-byte steps:
    /// ~0.5ms for the full 4MB frame (the GCHandle-pinned memcmp measured
    /// 20ms — the Pinned bookkeeping dominated).</summary>
    private static unsafe bool TileDiffers(byte* prev, byte* cur, int pitch, int tx, int ty, int tw, int th)
    {
        int x0 = tx * TileW;
        int y0 = ty * TileH;
        int bytes = tw * 4;
        int longs = bytes / 8 * 8;
        for (int y = 0; y < th; y++)
        {
            var pa = (ulong*)(prev + (y0 + y) * pitch + x0 * 4);
            var ca = (ulong*)(cur + (y0 + y) * pitch + x0 * 4);
            for (int i = 0; i < longs / 8; i++)
            {
                if (pa[i] != ca[i]) return true;
            }
            for (int i = longs; i < bytes; i++)
            {
                if (prev[(y0 + y) * pitch + x0 * 4 + i] != cur[(y0 + y) * pitch + x0 * 4 + i]) return true;
            }
        }
        return false;
    }

    private int TileWidth(int frameWidth, int tx) =>
        Math.Min(TileW, frameWidth - tx * TileW);

    private int TileHeight(int frameHeight, int ty) =>
        Math.Min(TileH, frameHeight - ty * TileH);

    private void RebuildScale(int w, int h)
    {
        if (_sws != null) sws_freeContext(_sws);
        if (_yuvFrame != null)
        {
            var f = _yuvFrame;
            av_frame_free(&f);
            _yuvFrame = null;
        }
        _sws = sws_getContext(w, h, AVPixelFormat.AV_PIX_FMT_BGRA,
            w, h, AVPixelFormat.AV_PIX_FMT_YUVJ420P,
            (int)SwsFlags.SWS_BILINEAR, null, null, null);
        _yuvFrame = av_frame_alloc();
        _yuvFrame->width = w;
        _yuvFrame->height = h;
        _yuvFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUVJ420P;
        av_frame_get_buffer(_yuvFrame, 0);
        // Tile sizes are frame-resolution-dependent: free the old contexts
        // before clearing (Clear alone leaked every AVCodecContext/AVFrame).
        foreach (var kv in _encoders)
        {
            var ctx = (AVCodecContext*)kv.Value;
            avcodec_free_context(&ctx);
        }
        _encoders.Clear();
        foreach (var kv in _tileFrames)
        {
            var f = (AVFrame*)kv.Value;
            av_frame_free(&f);
        }
        _tileFrames.Clear();
    }

    /// <summary>Quality ladder: pacer-queue pressure sheds JPEG quality
    /// instead of frames (down instantly, up only after a sustained idle —
    /// blur heals in a second, stutter is user-visible). At the floor it
    /// also halves the frame rate: q48 heavy content still exceeds the
    /// WiFi budget (140KB x 60fps = 8.5MB/s > 4.4MB/s), and skipping every
    /// other frame is the only lever left that fits. Backlog 0-1 is normal
    /// encode/pacer pipelining, not congestion — the downgrade thresholds
    /// start at 2 (the queue drops frames outright at 4).</summary>
    private void UpdateQualityLadder()
    {
        var backlog = TileBacklogProvider?.Invoke() ?? 0;
        if (backlog >= QualityLevels.Length)
        {
            _idleFrames = 0;
            SetQualityLevel(QualityLevels.Length - 1, backlog);
            return;
        }
        if (backlog >= 2)
        {
            _idleFrames = 0;
            SetQualityLevel(Math.Max(_qualityLevel, backlog - 1), backlog);
            return;
        }
        if (_qualityLevel == 0) return;
        if (++_idleFrames >= UpgradeIdleFrames)
        {
            _idleFrames = 0;
            SetQualityLevel(_qualityLevel - 1, 0);
        }
    }

    private void SetQualityLevel(int level, int backlog)
    {
        if (level == _qualityLevel) return;
        _qualityLevel = level;
        Log.Info($"[Tile] quality ladder -> q={QualityLevels[level]} (backlog={backlog})");
    }

    /// <summary>Get (or create) the mjpeg encoder context for a tile size at a
    /// quality level (global_quality is baked at open time; MJPEG has no
    /// reliable per-frame quality path, so each rung owns its contexts).</summary>
    private bool GetEncoder(int w, int h, int level)
    {
        var key = (w, h, level);
        if (_encoders.ContainsKey(key)) return true;

        var codec = avcodec_find_encoder(AVCodecID.AV_CODEC_ID_MJPEG);
        if (codec == null)
        {
            Log.Error("[Tile] mjpeg encoder not found in FFmpeg build");
            return false;
        }
        var ctx = avcodec_alloc_context3(codec);
        ctx->width = w;
        ctx->height = h;
        ctx->time_base = new AVRational { num = 1, den = 30 };
        ctx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUVJ420P;
        // qscale quality path: global_quality = quality * FF_QP2LAMBDA.
        ctx->global_quality = QualityLevels[level] * FF_QP2LAMBDA;
        ctx->flags |= AV_CODEC_FLAG_QSCALE;
        if (avcodec_open2(ctx, codec, null) < 0)
        {
            Log.Error("[Tile] mjpeg avcodec_open2 failed");
            avcodec_free_context(&ctx);
            return false;
        }
        _encoders[key] = (IntPtr)ctx;

        var frame = av_frame_alloc();
        frame->width = w;
        frame->height = h;
        frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUVJ420P;
        av_frame_get_buffer(frame, 0);
        _tileFrames[key] = (IntPtr)frame;
        return true;
    }

    /// <summary>Slice the tile's YUV planes into a tile frame and mjpeg-encode it.</summary>
    private byte[]? EncodeTile(int frameW, int frameH, int tx, int ty, int tw, int th, int qualityLevel)
    {
        if (!GetEncoder(tw, th, qualityLevel)) return null;
        var ctx = (AVCodecContext*)_encoders[(tw, th, qualityLevel)];
        var tileFrame = (AVFrame*)_tileFrames[(tw, th, qualityLevel)];

        int x0 = tx * TileW;
        int y0 = ty * TileH;
        // Y plane: direct row copy.
        for (int y = 0; y < th; y++)
        {
            Buffer.MemoryCopy(
                (void*)(_yuvFrame->data[0] + (y0 + y) * _yuvFrame->linesize[0] + x0),
                (void*)(tileFrame->data[0] + y * tileFrame->linesize[0]),
                tw, tw);
        }
        // U/V planes: 2× subsampled.
        int cx = x0 / 2, cy = y0 / 2, cw = tw / 2, ch = th / 2;
        for (int p = 1; p <= 2; p++)
        {
            for (int y = 0; y < ch; y++)
            {
                Buffer.MemoryCopy(
                    (void*)(_yuvFrame->data[(uint)p] + (cy + y) * _yuvFrame->linesize[(uint)p] + cx),
                    (void*)(tileFrame->data[(uint)p] + y * tileFrame->linesize[(uint)p]),
                    (nuint)cw, (nuint)cw);
            }
        }

        if (avcodec_send_frame(ctx, tileFrame) < 0) return null;
        var pkt = av_packet_alloc();
        try
        {
            var outBytes = new List<byte[]>();
            while (avcodec_receive_packet(ctx, pkt) == 0)
            {
                var chunk = new byte[pkt->size];
                Marshal.Copy((nint)pkt->data, chunk, 0, pkt->size);
                outBytes.Add(chunk);
                av_packet_unref(pkt);
            }
            if (outBytes.Count == 0) return null;
            var total = new byte[outBytes.Sum(b => b.Length)];
            int pos = 0;
            foreach (var b in outBytes) { Buffer.BlockCopy(b, 0, total, pos, b.Length); pos += b.Length; }
            return total;
        }
        finally
        {
            av_packet_free(&pkt);
        }
    }

    /// <summary>Chunk a tile's JPEG into wire payloads with the tile header:
    /// [tileIdx u16][tileCount u16][chunkIdx u8][chunkCount u8][jpeg...][flags u8].
    /// The flags byte is the LAST byte of the payload (patched per frame).</summary>
    private void ChunkTile(List<byte[]> packets, int tileIdx, int tileCount, byte[] jpeg)
    {
        int dataBudget = ChunkBudget;
        int chunks = (jpeg.Length + dataBudget - 1) / dataBudget;
        for (int c = 0; c < chunks; c++)
        {
            int len = Math.Min(dataBudget, jpeg.Length - c * dataBudget);
            var pkt = new byte[6 + len + 1];
            pkt[0] = (byte)(tileIdx >> 8);
            pkt[1] = (byte)tileIdx;
            pkt[2] = (byte)(tileCount >> 8);
            pkt[3] = (byte)tileCount;
            pkt[4] = (byte)c;
            pkt[5] = (byte)chunks;
            Buffer.BlockCopy(jpeg, c * dataBudget, pkt, 6, len);
            pkt[^1] = 0; // flags (last-of-frame patched by the caller)
            packets.Add(pkt);
        }
    }

    public void Dispose()
    {
        _running = false;
        _workSignal.Release();
        // Unbounded join: freeing the FFmpeg contexts while the worker is
        // mid-encode is a use-after-free (coreclr 0xc0000005 — same crash
        // class NvEncEncoder hit on viewer disconnect; the reconnect path
        // tears the session down mid-frame).
        _worker?.Join();
        _worker = null;
        lock (_encoders)
        {
            foreach (var kv in _encoders)
            {
                var ctx = (AVCodecContext*)kv.Value;
                avcodec_free_context(&ctx);
            }
            _encoders.Clear();
            foreach (var kv in _tileFrames)
            {
                var f = (AVFrame*)kv.Value;
                av_frame_free(&f);
            }
            _tileFrames.Clear();
            if (_sws != null) { sws_freeContext(_sws); _sws = null; }
            if (_yuvFrame != null)
            {
                var f = _yuvFrame;
                av_frame_free(&f);
                _yuvFrame = null;
            }
        }
        PacketEncoded = null;
    }
}
