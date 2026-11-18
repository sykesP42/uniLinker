using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

public class ScreenMirrorPlugin : IPlugin
{
    private IPluginContext? _ctx;
    private WgcCapture? _capture;
    private IEncoder? _encoder;

    private readonly object _sessionGate = new();
    private readonly List<IChannel> _viewers = new();

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
        context.Logger.Info("ScreenMirror plugin initialized");
        return Task.FromResult(true);
    }

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
                if (channel is IDirectRtpChannel direct &&
                    (direct.RequestedWidth > 0 || direct.RequestedBitrateKbps > 0 || direct.RequestedFps > 0))
                {
                    fps = direct.RequestedFps > 0 ? direct.RequestedFps : fps;
                    bitrate = direct.RequestedBitrateKbps > 0 ? direct.RequestedBitrateKbps : bitrate;
                    _ctx.Logger.Info($"Receiver tier: {direct.RequestedWidth}x{direct.RequestedHeight}@{fps} {bitrate}kbps");
                }
                StartSession(fps, bitrate);
            }
            _ctx.Logger.Info($"Viewer added (total {_viewers.Count}) for {peer.Name}");
        }

        channel.OnClose += () => ReleaseViewer(channel);
        // Emit an IDR right away so the joining viewer can decode immediately.
        _encoder?.ForceKeyFrame();
        return channel;
    }

    private void StartSession(int fps, int bitrateKbps)
    {
        _capture = new WgcCapture();
        // Capture at the configured resolution. The frame pool is created at
        // this size and WGC scales the monitor content into it — capturing at
        // the native 2560x1600 while the bitrate targets a 720p-class tier
        // wrecks both encode latency and quality (measured: this was the
        // dominant bottleneck of the wired link).
        var config = _ctx?.Config.Get<ScreenMirrorConfig>("screen-mirror");
        var captureW = config?.CaptureWidth > 0 ? config.CaptureWidth : 0;
        var captureH = config?.CaptureHeight > 0 ? config.CaptureHeight : 0;
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
    }

    /// <summary>
    /// Encoder selection: "nvenc" forces hardware (fail the session if it
    /// cannot open), "openh264" forces CPU, "auto" (default) tries NVENC
    /// first and silently falls back to the CPU encoder — the 60fps unlock
    /// with a guaranteed floor.
    /// </summary>
    private IEncoder? CreateEncoder(ScreenMirrorConfig? config, int width, int height, int fps, int bitrateKbps)
    {
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
                if (nvenc.Initialize(width, height, fps, bitrateKbps))
                {
                    _ctx?.Logger.Info($"Encoder: NVENC {width}x{height}@{fps} {bitrateKbps}kbps");
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
        // Dispose chains can race the capture/encoder worker threads; a throw
        // here must not leave the session half-torn-down nor break the caller
        // (direct-RTP reconnect disposes the stale channel before rebinding).
        try
        {
            _capture?.Dispose();
        }
        catch (Exception ex)
        {
            _ctx?.Logger.Error($"capture dispose threw: {ex.Message}", ex);
        }
        _capture = null;
        try
        {
            _encoder?.Dispose();
        }
        catch (Exception ex)
        {
            _ctx?.Logger.Error($"encoder dispose threw: {ex.Message}", ex);
        }
        _encoder = null;
        _ctx?.Logger.Info("Screen sharing session stopped");
    }

    public Task<IChannel?> OnPeerRequest(PeerInfo peer, string capability)
        => Task.FromResult<IChannel?>(null);

    public async Task Shutdown()
    {
        _ctx!.Peers.ChannelRequested -= OnChannelRequested;
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
}
