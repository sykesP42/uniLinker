using UniLinker.Core;

namespace UniLinker.WinUI.Services;

public class AppServices
{
    public Platform Platform { get; }
    public WebBridge Bridge { get; }
    public SignalingServer SignalingServer { get; }
    public NavigationService Navigation { get; }

    public AppServices(Platform platform, WebBridge bridge, SignalingServer signalingServer)
    {
        Platform = platform;
        Bridge = bridge;
        SignalingServer = signalingServer;
        Navigation = new NavigationService();
    }
}