using UniLinker.Core;
using UniLinker.UI;

namespace UniLinker.App;

public class AppHost
{
    public static async Task RunAsync(string[] args)
    {
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "uniLinker", "config.json");

        using var platform = new Platform(pluginsDir, configPath);
        await platform.StartAsync();

        var signalingServer = new SignalingServer(
            9527, platform.PeerMesh!);
        _ = signalingServer.StartAsync();

        var bridge = new WebBridge(platform);

        // Ensure UI project builds and references are correct
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var window = new MainWindow(bridge);
        window.FormClosing += async (_, _) =>
        {
            signalingServer.Dispose();
            await platform.StopAsync();
        };

        Application.Run(window);
    }
}
