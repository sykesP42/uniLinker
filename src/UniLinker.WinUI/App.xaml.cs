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
    private SignalingKestrelHost? _signalingServer;
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
        DisablePowerThrottling();
    }

    // Windows 11 power-throttles an UNFOCUSED window's process (EcoQoS):
    // threads land on efficiency cores at low clocks, Thread.Sleep and even
    // SpinWait stretch ~20x, and timeBeginPeriod stops applying — the whole
    // mirroring pipeline (pacer timing, WGC poll timer, encode threads)
    // silently degraded whenever the user looked at the tablet instead of
    // this window. A screen-mirroring SERVER must never be power-throttled.
    private static void DisablePowerThrottling()
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = 1,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = 0, // 0 = throttling disabled for this control
            };
            SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling,
                ref state, System.Runtime.InteropServices.Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch
        {
            // Pre-Win10-20H1 systems without the API: nothing to opt out of.
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetProcessInformation(
        nint hProcess, int infoClass,
        ref PROCESS_POWER_THROTTLING_STATE info, int size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    private const int ProcessPowerThrottling = 1; // PROCESS_INFORMATION_CLASS
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
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

            // Inject after Platform start: the SIPSorcery assembly is loaded
            // by then (its internal Log is what we need to capture).
            var sipLogProvider = new SipFileLoggerProvider(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "uniLinker", "sipsorcery.log"));
            sipLogProvider.InjectIntoSipsorcery();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Platform start failed: {ex.Message}");
        }

        var config = _platform.Context.Config.Get<PlatformConfig>("platform");

        _bridge = new WebBridge(_platform);
        // Device identity (M4): self-signed cert persisted DPAPI-encrypted;
        // its fingerprint is the device identity and the pairing code is
        // derived from it (shown on-screen to the human during pairing).
        var identity = UniLinker.Core.DeviceIdentity.LoadOrCreate(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "uniLinker", UniLinker.Core.DeviceIdentity.DefaultStoreSubdirectory));
        Log.Info($"Device fingerprint: {identity.FingerprintSha256}");
        Log.Info($"Pairing code: {identity.PairingCode}");

        _signalingServer = new SignalingKestrelHost(
            config.HttpPort > 0 ? config.HttpPort : 9527,
            _platform.PeerMesh,
            identity.Certificate);

        Services = new AppServices(_platform, _bridge, _signalingServer);

        // Start the signaling server (Kestrel host, M2 migration).
        try
        {
            await _signalingServer.StartAsync();
        }
        catch (Exception ex)
        {
            // Debug.WriteLine is invisible in an unpackaged GUI app — put the
            // failure where diagnostics can actually see it.
            Log.Info($"Failed to start signaling server: {ex}");
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
                var runtimeIdentity = UniLinker.Core.DeviceIdentity.LoadOrCreate(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "uniLinker", UniLinker.Core.DeviceIdentity.DefaultStoreSubdirectory));
                app._signalingServer = new SignalingKestrelHost(
                    httpPort, app._platform.PeerMesh, runtimeIdentity.Certificate);
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
                    // Direct-RTP transport by core stack: sipsorcery | rtx.
                    UniLinker.Plugin.Sdk.DirectRtpTransport.Mode = settings.TransportIndex switch
                    {
                        0 => "auto",
                        1 => "sipsorcery",
                        2 => "rtx",
                        3 => "tile",
                        _ => "auto"
                    };
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
        /// <summary>Direct-RTP transport: 0 = Native RTX, 1 = SIPSorcery, 2 = Tile.</summary>
        public int TransportIndex { get; set; } = 0; // auto (recommended default)
    }
}

