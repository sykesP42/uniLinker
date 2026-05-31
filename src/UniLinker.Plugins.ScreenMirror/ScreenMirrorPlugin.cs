using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

public class ScreenMirrorPlugin : IPlugin
{
    private IPluginContext? _ctx;
    private WgcCapture? _capture;
    private MfEncoder? _encoder;
    private bool _isSharing;

    public string Id => "com.unilinker.screen-mirror";
    public string Name => "屏幕投屏";
    public string Version => "1.0.0";
    public PluginPermission RequiredPermissions => PluginPermission.ScreenCapture;
    public string[] Capabilities => ["screen-capture", "h264-encode", "video-stream"];

    public Task<bool> Initialize(IPluginContext context)
    {
        _ctx = context;
        _capture = new WgcCapture();
        _encoder = new MfEncoder();

        _ctx.UI.RegisterPanel(Id, new PanelInfo
        {
            Title = "屏幕投屏",
            Icon = "📺",
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
        var width = config.CaptureWidth > 0 ? config.CaptureWidth : 1920;
        var height = config.CaptureHeight > 0 ? config.CaptureHeight : 1080;
        var fps = config.CaptureFps > 0 ? config.CaptureFps : 30;
        var bitrate = config.BitrateKbps > 0 ? config.BitrateKbps : 15000;

        _capture!.Start(width, height, fps);
        _encoder!.Initialize(width, height, fps, bitrate);

        var channel = await _ctx.Peers.CreateChannel(peer, capability);
        if (channel != null)
        {
            _isSharing = true;

            // Wire: capture -> encode -> send
            _capture.FrameCaptured += frame =>
            {
                if (_isSharing)
                    _encoder.Encode(frame);
            };

            _encoder.PacketEncoded += async packet =>
            {
                if (_isSharing && channel.IsOpen)
                    await channel.SendPacketAsync(packet);
            };
        }

        return channel;
    }

    public Task<IChannel?> OnPeerRequest(PeerInfo peer, string capability)
        => Task.FromResult<IChannel?>(null);

    public async Task Shutdown()
    {
        _isSharing = false;
        _capture?.Dispose();
        _encoder?.Dispose();
        await Task.CompletedTask;
    }
}

public class ScreenMirrorConfig
{
    public int CaptureWidth { get; set; }
    public int CaptureHeight { get; set; }
    public int CaptureFps { get; set; } = 30;
    public int BitrateKbps { get; set; } = 15000;
}
