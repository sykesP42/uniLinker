using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel;
using UniLinker.Core;
using UniLinker.WinUI.Services;

namespace UniLinker.WinUI;

public partial class App : Application
{
    private Platform? _platform;
    private WebBridge? _bridge;
    private SignalingServer? _signalingServer;
    private MainWindow? _window;

    public static AppServices Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "uniLinker", "config.json");

        _platform = new Platform(pluginsDir, configPath);
        _bridge = new WebBridge(_platform);
        _signalingServer = new SignalingServer(9527, _platform.PeerMesh);

        Services = new AppServices(_platform, _bridge, _signalingServer);

        await _platform.StartAsync();

        _window = new MainWindow();
        _window.Activate();
    }
}