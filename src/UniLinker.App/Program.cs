using Avalonia;
using UniLinker.Core;
using UniLinker.UI;

namespace UniLinker.App;

class Program
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<UI.App>()
            .UsePlatformDetect()
            .LogToTrace();

    [STAThread]
    public static void Main(string[] args)
    {
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "uniLinker", "config.json");

        var platform = new Platform(pluginsDir, configPath);
        var bridge = new WebBridge(platform);
        var signalingServer = new SignalingServer(9527, platform.PeerMesh!);

        // Initialize services before Avalonia starts
        UI.App.InitializeServices(platform, bridge, signalingServer);

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }
}