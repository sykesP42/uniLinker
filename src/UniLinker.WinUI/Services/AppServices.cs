using UniLinker.Core;

namespace UniLinker.WinUI.Services;

public class AppServices
{
    public Platform Platform { get; }
    public WebBridge Bridge { get; }
    public SignalingKestrelHost SignalingServer { get; set; }
    public NavigationService Navigation { get; }

    public AppServices(Platform platform, WebBridge bridge, SignalingKestrelHost signalingServer)
    {
        Platform = platform;
        Bridge = bridge;
        SignalingServer = signalingServer;
        Navigation = new NavigationService();
    }
}
