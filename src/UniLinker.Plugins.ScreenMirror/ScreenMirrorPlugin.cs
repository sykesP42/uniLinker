using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

public class ScreenMirrorPlugin : IPlugin
{
    private IPluginContext? _ctx;
    private WgcCapture? _capture;
    private IEncoder? _encoder;

    // Windows Thread.Sleep granularity is ~15.6ms unless the process raises
    // the timer resolution — the encoder worker paces at 33ms and overshoots
    // to ~46ms (16fps instead of 30). Raise it for this process's lifetime.
    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint msPeriod);

    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint msPeriod);

    private readonly object _sessionGate = new();
    private readonly List<IChannel> _viewers = new();

    // Viewer liveness watchdog (ViewerLiveness): a receiver killed without a
    // disconnect never fires channel.OnClose, so the session used to encode
    // and blast media into the void for hours. Beats arrive every 2 s; 8 s
    // of silence (4 missed beats) means the viewer is gone.
    private const int ViewerIdleTimeoutMs = 8000;
    private long _lastViewerBeatMs;
    private CancellationTokenSource? _watchdogCts;

    public string Id => "com.unilinker.screen-mirror";
    public string Name => "Screen Mirror";
    public string Version => "1.0.0";
    public PluginPermission RequiredPermissions => PluginPermission.ScreenCapture;
    public string[] Capabilities => ["screen-capture", "h264-encode", "video-stream"];

    public Task<bool> Initialize(IPluginContext context)
    {
        _ctx = context;

        _ctx.UI.RegisterPanel(Id, new PanelInfo
        {
            Title = "Screen Mirror",
            Icon = "\U0001F5A5",
            HtmlPath = "plugins/screen-mirror/panel.html",
        });

        _ctx.Peers.ChannelRequested += OnChannelRequested;
        // Media-plane PLI: a loss-detecting receiver asks for an IDR via
        // /ulp/pli (rate-limited in KeyFrameBroker) instead of waiting for
        // the periodic GOP boundary.
        KeyFrameBroker.KeyFrameRequested += OnKeyFrameRequested;
        // Viewer liveness: any /ulp/* request from the viewer stamps the beat.
        ViewerLiveness.Heartbeat += OnViewerHeartbeat;
        timeBeginPeriod(1);
        context.Logger.Info("ScreenMirror plugin initialized");
        return Task.FromResult(true);
    }

    private void OnKeyFrameRequested() => _encoder?.ForceKeyFrame();

    private void OnViewerHeartbeat() => Volatile.Write(ref _lastViewerBeatMs, Environment.TickCount64);

    private async Task<IChannel?> OnChannelRequested(PeerInfo peer, string capability)
    {
        if (capability != "screen-capture" || _ctx == null) return null;

        _ctx.Logger.Info($"Screen capture requested by {peer.Name}");

        var config = _ctx.Config.Get<ScreenMirrorConfig>("screen-mirror");
        var fps = config.CaptureFps > 0 ? config.CaptureFps : 30;
        var bitrate = config.BitrateKbps > 0 ? config.BitrateKbps : 8000;

        // ConfigureAwait(false): the continuation must NOT resume on the WinUI
        // STA UI thread — NvEncEncoder's avcodec_open2 (NVENC/CUDA init)
        // deadlocks there (driver call marshaled back to the pumping STA,
        // empirically verified 2026-11-19). Everything downstream
        // (WGC CreateFreeThreaded, encoders, channels) is threadpool-safe.
        var channel = await _ctx.Peers.CreateChannel(peer, capability).ConfigureAwait(false);
        if (channel == null)
        {
            _ctx.Logger.Warn($"Failed to create channel for {peer.Name}");
            return null;
        }

        lock (_sessionGate)
        {
            _viewers.Add(channel);
            if (_viewers.Count == 1)
            {
                // Receiver-driven tier: a direct-RTP channel carries the
                // capture parameters the receiver requested (resolution/
                // bitrate/fps). Zero values fall back to the sender config.
                int tierW = 0, tierH = 0;
                if (channel is IDirectRtpChannel direct &&
                    (direct.RequestedWidth > 0 || direct.RequestedBitrateKbps > 0 || direct.RequestedFps > 0))
                {
                    fps = direct.RequestedFps > 0 ? direct.RequestedFps : fps;
                    bitrate = direct.RequestedBitrateKbps > 0 ? direct.RequestedBitrateKbps : bitrate;
                    // Resolution is receiver-driven too: it was read for the
                    // log only for months — the capture always ran at the PC
                    // config's size and the tier's width/height did nothing.
                    tierW = direct.RequestedWidth;
                    tierH = direct.RequestedHeight;
                    _ctx.Logger.Info($"Receiver tier: {direct.RequestedWidth}x{direct.RequestedHeight}@{fps} {bitrate}kbps");
                }
                StartSession(fps, bitrate, tierW, tierH);
            }
            _ctx.Logger.Info($"Viewer added (total {_viewers.Count}) for {peer.Name}");
        }

        channel.OnClose += () => ReleaseViewer(channel);
        // Emit IDRs so the joining viewer can decode immediately. ONE IDR is
        // not enough: the bind race (the receiver socket may open after the
        // IDR is on the wire — the M3 lesson) plus the 10-minute GOP meant a
        // mid-stream join could wait MINUTES for the next keyframe, the
        // decoder stuck at decoded=0 with no seq gap to trigger a PLI.
        // Three IDRs over ~600ms cover the race (the broker rate-limits to
        // 250ms, so this fans out to at most 3 real keyframes).
        _encoder?.ForceKeyFrame();
        _ = Task.Run(async () =>
        {
            foreach (var delayMs in new[] { 300, 600 })
            {
                await Task.Delay(delayMs);
                _encoder?.ForceKeyFrame();
            }
        });
        return channel;
    }

    private void StartSession(int fps, int bitrateKbps, int tierW = 0, int tierH = 0)
    {
        // A reconnect (a new viewer arriving while the previous session
        // still lives — the stale tablet channel may not have fired OnClose
        // yet) used to LEAK the previous capture/encoder: their threads
        // kept running and the old encoder's PacketEncoded (which snapshots
        // _viewers at emit time) interleaved a SECOND stream into the same
        // receiver — measured as 114fps with two SSRCs and rising
        // corruptDropped (2026-11-28). At most one live session, ever.
        if (_capture != null || _encoder != null)
        {
            _ctx?.Logger.Warn("StartSession while a session is live — tearing it down first");
            StopSession();
        }

        _capture = new WgcCapture();
        // Capture resolution precedence: the receiver's tier (drives capture
        // AND encoder grid) > the PC config > native. The frame pool is
        // created at this size and WGC scales the monitor content into it —
        // capturing at the native 2560x1600 while the bitrate targets a
        // 720p-class tier wrecks both encode latency and quality (measured:
        // this was the dominant bottleneck of the wired link).
        var config = _ctx?.Config.Get<ScreenMirrorConfig>("screen-mirror");
        var captureW = tierW > 0 ? tierW : (config?.CaptureWidth > 0 ? config.CaptureWidth : 0);
        var captureH = tierH > 0 ? tierH : (config?.CaptureHeight > 0 ? config.CaptureHeight : 0);
        if (!_capture.Start(captureW, captureH, fps))
        {
            _ctx?.Logger.Error("WGC capture failed to start", null);
            _capture.Dispose();
            _capture = null;
            return;
        }

        _encoder = CreateEncoder(config, _capture.OutputWidth, _capture.OutputHeight, fps, bitrateKbps);
        if (_encoder == null)
        {
            _capture.Dispose();
            _capture = null;
            return;
        }

        // Quality ladder pressure: the tile encoder sheds JPEG quality when
        // any viewer's pacer queue backs up (the commercial degradation
        // model — blur instead of stutter). Max across viewers: the most
        // congested link governs.
        if (_encoder is TileEncoder tileEnc)
        {
            tileEnc.TileBacklogProvider = () =>
            {
                lock (_sessionGate)
                {
                    var max = 0;
                    foreach (var v in _viewers)
                        if (v is IDirectRtpChannel d && d.TileBacklog > max) max = d.TileBacklog;
                    return max;
                }
            };
        }

        _capture.FrameCaptured += frame => _encoder?.Encode(frame);
        _encoder.PacketEncoded += packet =>
        {
            IChannel[] viewers;
            lock (_sessionGate)
            {
                viewers = [.. _viewers];
            }
            foreach (var viewer in viewers)
            {
                if (viewer.IsOpen)
                {
                    _ = SendSafeAsync(viewer, packet);
                }
            }
        };

        _ctx?.Logger.Info("Screen sharing session started");

        // Arm the liveness watchdog with a full grace window: the receiver
        // sends its first beat ~2 s after STREAMING.
        Volatile.Write(ref _lastViewerBeatMs, Environment.TickCount64);
        _watchdogCts = new CancellationTokenSource();
        var ct = _watchdogCts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(2000, ct).ConfigureAwait(false); }
                catch (TaskCanceledException) { return; }
                var idleMs = Environment.TickCount64 - Volatile.Read(ref _lastViewerBeatMs);
                if (idleMs < ViewerIdleTimeoutMs) continue;
                _ctx?.Logger.Warn($"Viewer idle {idleMs}ms with no keepalive — releasing stale session");
                ReleaseAllViewers();
                return;
            }
        });
    }

    /// <summary>Idle-timeout teardown: close every viewer channel (OnClose →
    /// ReleaseViewer → StopSession) and force the cleanup should a channel
    /// swallow its close event.</summary>
    private void ReleaseAllViewers()
    {
        IChannel[] stale;
        lock (_sessionGate)
        {
            stale = [.. _viewers];
        }
        foreach (var channel in stale)
        {
            try { channel.Dispose(); } // Dispose raises OnClose → ReleaseViewer → StopSession
            catch (Exception ex) { _ctx?.Logger.Warn($"viewer channel close failed: {ex.Message}"); }
        }
        lock (_sessionGate)
        {
            if (_viewers.Count == 0) return;
            _viewers.Clear();
        }
        StopSession();
    }

    /// <summary>
    /// Encoder selection: "nvenc" forces hardware (fail the session if it
    /// cannot open), "openh264" forces CPU, "auto" (default) tries NVENC
    /// first and silently falls back to the CPU encoder — the 60fps unlock
    /// with a guaranteed floor.
    /// </summary>
    private IEncoder? CreateEncoder(ScreenMirrorConfig? config, int width, int height, int fps, int bitrateKbps)
    {
        // Tile transport uses its own encoder (JPEG tiles), regardless of
        // the codec setting which only governs the H264 stream modes.
        if (DirectRtpTransport.Mode == "tile")
        {
            var tile = new TileEncoder();
            if (tile.Initialize(width, height, fps, bitrateKbps))
            {
                _ctx?.Logger.Info($"Encoder: Tile JPEG {width}x{height}@{fps} grid {width / TileEncoder.TileW}x{height / TileEncoder.TileH}");
                return tile;
            }
            tile.Dispose();
            _ctx?.Logger.Warn("Tile encoder init failed — falling back to the codec ladder");
        }

        var choice = (config?.Encoder ?? "auto").ToLowerInvariant();
        _ctx?.Logger.Info($"CreateEncoder: choice={choice} {width}x{height}@{fps} {bitrateKbps}kbps");
        if (choice == "openh264")
        {
            return StartOpenH264(fps, bitrateKbps);
        }

        if (choice == "nvenc" || choice == "auto")
        {
            try
            {
                var nvenc = new NvEncEncoder();
                // Zero-copy: hand the capture's D3D11 device/context over —
                // a successful hw open switches the capture to GPU delivery
                // (CaptureFrame.D3dTexture); failure falls back to the CPU
                // readback path with the same encoder.
                var zeroCopy = (config?.EncoderCopy ?? "auto") == "d3d11";
                if (nvenc.Initialize(width, height, fps, bitrateKbps,
                        zeroCopy ? _capture?.NativeDevicePointer ?? 0 : 0,
                        zeroCopy ? _capture?.NativeContextPointer ?? 0 : 0,
                        zeroCopy ? _capture?.GpuGate : null))
                {
                    if (_capture != null) _capture.PreferGpuDelivery = nvenc.UsesGpuFrames;
                    _ctx?.Logger.Info($"Encoder: NVENC {width}x{height}@{fps} {bitrateKbps}kbps gpu={(nvenc.UsesGpuFrames ? "zero-copy" : "system-memory")}");
                    return nvenc;
                }
                nvenc.Dispose();
                _ctx?.Logger.Warn("NVENC unavailable — falling back to OpenH264 (CPU)");
                if (choice == "nvenc") return null;
            }
            catch (Exception ex)
            {
                _ctx?.Logger.Error($"NVENC init threw ({ex.GetType().Name}: {ex.Message}) — falling back to OpenH264", ex);
            }
        }

        return StartOpenH264(fps, bitrateKbps);
    }

    private IEncoder StartOpenH264(int fps, int bitrateKbps)
    {
        var encoder = new OpenH264Encoder();
        encoder.Initialize(0, 0, fps, bitrateKbps);
        return encoder;
    }

    private async Task SendSafeAsync(IChannel viewer, EncodedPacket packet)
    {
        try
        {
            await viewer.SendPacketAsync(packet);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ScreenMirror] send to {viewer.RemotePeer.Name} failed: {ex.Message}");
        }
    }

    private void ReleaseViewer(IChannel channel)
    {
        lock (_sessionGate)
        {
            _viewers.Remove(channel);
            if (_viewers.Count > 0) return;
        }

        StopSession();
    }

    private void StopSession()
    {
        _ctx?.Logger.Info("Stopping screen sharing session");
        _watchdogCts?.Cancel();
        _watchdogCts = null;
        // Dispose chains can race the capture/encoder worker threads; a throw
        // here must not leave the session half-torn-down nor break the caller
        // (direct-RTP reconnect disposes the stale channel before rebinding).
        // Zero-copy ordering: the encoder BORROWS the capture's D3D11
        // device/context — free the encoder (its hw refs + borrowed
        // wrappers) BEFORE the capture destroys those objects.
        _ctx?.Logger.Info("session stop: disposing encoder");
        try
        {
            _encoder?.Dispose();
            _ctx?.Logger.Info("session stop: encoder disposed");
        }
        catch (Exception ex)
        {
            _ctx?.Logger.Error($"encoder dispose threw: {ex.Message}", ex);
        }
        _encoder = null;
        _ctx?.Logger.Info("session stop: disposing capture");
        try
        {
            _capture?.Dispose();
            _ctx?.Logger.Info("session stop: capture disposed");
        }
        catch (Exception ex)
        {
            _ctx?.Logger.Error($"capture dispose threw: {ex.Message}", ex);
        }
        _capture = null;
        _ctx?.Logger.Info("Screen sharing session stopped");
    }

    public Task<IChannel?> OnPeerRequest(PeerInfo peer, string capability)
        => Task.FromResult<IChannel?>(null);

    public async Task Shutdown()
    {
        _ctx!.Peers.ChannelRequested -= OnChannelRequested;
        KeyFrameBroker.KeyFrameRequested -= OnKeyFrameRequested;
        ViewerLiveness.Heartbeat -= OnViewerHeartbeat;
        timeEndPeriod(1);
        lock (_sessionGate)
        {
            _viewers.Clear();
        }
        StopSession();
        await Task.CompletedTask;
    }
}

public class ScreenMirrorConfig
{
    public int CaptureWidth { get; set; }
    public int CaptureHeight { get; set; }
    public int CaptureFps { get; set; } = 30;
    public int BitrateKbps { get; set; } = 8000;
    /// <summary>"auto" (NVENC first, CPU fallback) | "nvenc" | "openh264".</summary>
    public string Encoder { get; set; } = "auto";
    /// <summary>Zero-copy D3D11 delivery to nvenc. "auto" keeps the
    /// verified system-memory BGRA path — the d3d11 hwframes path still
    /// AVs in CopySubresourceRegion on this build (slice 7 pinned, pool
    /// not recycling; suspected the mandatory AVD3D11VADeviceContext
    /// lock/unlock callbacks being unset). "d3d11" opts in for debugging.</summary>
    public string EncoderCopy { get; set; } = "auto";
}
