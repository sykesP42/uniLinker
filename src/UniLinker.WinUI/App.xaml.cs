using Microsoft.UI.Xaml;
using UniLinker.Core;
using UniLinker.Plugins.FileTransfer;
using UniLinker.Plugins.ScreenMirror;
using UniLinker.WinUI.Services;

namespace UniLinker.WinUI;

public partial class App : Application
{
    private Platform? _platform;
    private WebBridge? _bridge;
    private SignalingServer? _signalingServer;
    private MainWindow? _window;

    public static AppServices Services { get; private set; } = null!;

    /// <summary>
    /// Handle of the main window (for pickers and other window-scoped APIs).
    /// </summary>
    public static nint MainWindowHandle { get; internal set; }

    // Theme management
    public static event EventHandler<ElementTheme>? ThemeChanged;
    private static ElementTheme _currentTheme = ElementTheme.Default;
    private static MainWindow? _currentWindow;

    public static ElementTheme CurrentTheme
    {
        get => _currentTheme;
        set
        {
            if (_currentTheme != value)
            {
                _currentTheme = value;
                ApplyTheme(value);
                ThemeChanged?.Invoke(null, value);
            }
        }
    }

    public App()
    {
        // Initialize the Windows App SDK for unpackaged apps
        // Note: For unpackaged apps, the runtime must be installed or deployed
        // See: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps
        CrashLogger.Initialize();
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "uniLinker", "config.json");

        _platform = new Platform(pluginsDir, configPath);

        // Built-in plugins (external ones are still loaded from plugins/).
        _platform.RegisterPlugin(new ScreenMirrorPlugin());
        _platform.RegisterPlugin(new FileTransferPlugin());

        try
        {
            await _platform.StartAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Platform start failed: {ex.Message}");
        }

        var config = _platform.Context.Config.Get<PlatformConfig>("platform");

        _bridge = new WebBridge(_platform);
        _signalingServer = new SignalingServer(
            config.HttpPort > 0 ? config.HttpPort : 9527,
            _platform.PeerMesh);

        Services = new AppServices(_platform, _bridge, _signalingServer);

        // Start the signaling server for WebRTC connections
        try
        {
            await _signalingServer.StartAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start signaling server: {ex.Message}");
        }

        _window = new MainWindow();
        _currentWindow = _window;

        // Load and apply saved theme
        LoadTheme();
        _window.Activate();
    }

    /// <summary>
    /// Stop the platform and signaling server (called on real app exit).
    /// </summary>
    public static async Task ShutdownAsync()
    {
        if (Current is App app)
        {
            try { app._signalingServer?.Stop(); } catch { }
            try { await app._platform?.StopAsync(); } catch { }
        }
    }

    /// <summary>
    /// Apply device name / port to the running platform. Restarts the
    /// signaling server when the port changed. Returns false when the
    /// platform is not running.
    /// </summary>
    public static async Task<bool> ApplyRuntimeSettingsAsync(string deviceName, int httpPort)
    {
        if (Current is not App app || app._platform == null) return false;

        var oldPort = app._signalingServer?.Port ?? 0;
        await app._platform.ApplySettingsAsync(deviceName, httpPort);

        if (httpPort > 0 && httpPort != oldPort)
        {
            try
            {
                app._signalingServer?.Stop();
                app._signalingServer = new SignalingServer(httpPort, app._platform.PeerMesh);
                Services = new AppServices(app._platform, app._bridge!, app._signalingServer);
                await app._signalingServer.StartAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Signaling restart failed: {ex.Message}");
                return false;
            }
        }
        return true;
    }

    private void LoadTheme()
    {
        // Load theme from settings
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "uniLinker", "settings.json");

        if (File.Exists(settingsPath))
        {
            try
            {
                var json = File.ReadAllText(settingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    _currentTheme = settings.ThemeIndex switch
                    {
                        0 => ElementTheme.Dark,
                        1 => ElementTheme.Light,
                        _ => ElementTheme.Default
                    };
                    ApplyTheme(_currentTheme);
                }
            }
            catch
            {
                // Use default theme if load fails
            }
        }
    }

    private static void ApplyTheme(ElementTheme theme)
    {
        if (_currentWindow != null)
        {
            // Apply theme to the window content
            if (_currentWindow.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = theme;
            }

            // Update title bar colors
            if (_currentWindow is MainWindow mainWindow)
            {
                mainWindow.UpdateTitleBarForTheme(theme);
            }
        }
    }

    internal class AppSettings
    {
        public int ThemeIndex { get; set; }
    }
}

