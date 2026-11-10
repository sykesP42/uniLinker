using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

public class ScreenMirrorPlugin : IPlugin
{
    private IPluginContext? _ctx;
    private WgcCapture? _capture;
    private OpenH264Encoder? _encoder;

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

        var channel = await _ctx.Peers.CreateChannel(peer, capability);
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
        // Capture at the display's native resolution; the encoder adopts the
        // real frame size on the first frame.
        if (!_capture.Start(0, 0, fps))
        {
            _ctx?.Logger.Error("WGC capture failed to start", null);
            _capture.Dispose();
            _capture = null;
            return;
        }

        _encoder = new OpenH264Encoder();
        _encoder.Initialize(0, 0, fps, bitrateKbps);

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
        _capture?.Dispose();
        _capture = null;
        _encoder?.Dispose();
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
}
