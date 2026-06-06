using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using System.Text.Json;
using UniLinker.WinUI.Services;

namespace UniLinker.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _deviceName = Environment.MachineName;
    [ObservableProperty] private int _httpPort = 9527;
    [ObservableProperty] private bool _autoStart = true;
    [ObservableProperty] private bool _minimizeToTray = true;

    [ObservableProperty] private int _defaultResolution = 0; // 0: 1080p, 1: 2K, 2: 4K
    [ObservableProperty] private int _defaultFps = 0;        // 0: 30fps, 1: 60fps
    [ObservableProperty] private int _defaultBitrate = 1;    // 0: 10M, 1: 15M, 2: 20M

    private int _themeIndex = 2; // Default to System
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (SetProperty(ref _themeIndex, value))
            {
                ApplyTheme(value);
            }
        }
    }

    private readonly WebBridge? _bridge;
    private readonly string _configPath;

    public SettingsViewModel()
    {
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "uniLinker", "settings.json");
    }

    public SettingsViewModel(WebBridge bridge) : this()
    {
        _bridge = bridge;
        LoadSettings();
    }

    public void LoadSettings()
    {
        try
        {
            // Load from config store via bridge
            if (_bridge != null)
            {
                var platformConfig = _bridge.GetStatusInfo();
                // DeviceName and HttpPort are in PlatformConfig
            }

            // Load local settings
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    DeviceName = settings.DeviceName ?? Environment.MachineName;
                    HttpPort = settings.HttpPort > 0 ? settings.HttpPort : 9527;
                    AutoStart = settings.AutoStart;
                    MinimizeToTray = settings.MinimizeToTray;
                    ThemeIndex = settings.ThemeIndex;
                    DefaultResolution = settings.DefaultResolution;
                    DefaultFps = settings.DefaultFps;
                    DefaultBitrate = settings.DefaultBitrate;
                }
            }
        }
        catch
        {
            // Use defaults if load fails
        }
    }

    private void ApplyTheme(int index)
    {
        // WinUI 3 theme is applied via the window's content
        // The theme will be applied when the app restarts or via the MainWindow
        if (App.Current is App app)
        {
            app.RequestedTheme = index switch
            {
                0 => ApplicationTheme.Dark,
                1 => ApplicationTheme.Light,
                _ => ApplicationTheme.Dark // System is not directly supported, default to Dark
            };
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var settings = new AppSettings
            {
                DeviceName = DeviceName,
                HttpPort = HttpPort,
                AutoStart = AutoStart,
                MinimizeToTray = MinimizeToTray,
                ThemeIndex = ThemeIndex,
                DefaultResolution = DefaultResolution,
                DefaultFps = DefaultFps,
                DefaultBitrate = DefaultBitrate
            };

            // Save local settings
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);

            // Update platform config via bridge
            var platformConfig = JsonSerializer.Serialize(new
            {
                DeviceName,
                HttpPort,
                AutoStartPlugins = true
            });
            _bridge?.SaveConfig(platformConfig);

            StatusMessage = "Settings saved successfully";
            HasUnsavedChanges = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetSettings()
    {
        DeviceName = Environment.MachineName;
        HttpPort = 9527;
        AutoStart = true;
        MinimizeToTray = true;
        ThemeIndex = 2; // System
        DefaultResolution = 0;
        DefaultFps = 0;
        DefaultBitrate = 1;
        StatusMessage = "Settings reset to defaults";
        HasUnsavedChanges = true;
    }

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasUnsavedChanges = false;

    // Called when any setting changes
    partial void OnDeviceNameChanged(string value) => HasUnsavedChanges = true;
    partial void OnHttpPortChanged(int value) => HasUnsavedChanges = true;
    partial void OnAutoStartChanged(bool value) => HasUnsavedChanges = true;
    partial void OnMinimizeToTrayChanged(bool value) => HasUnsavedChanges = true;
    partial void OnDefaultResolutionChanged(int value) => HasUnsavedChanges = true;
    partial void OnDefaultFpsChanged(int value) => HasUnsavedChanges = true;
    partial void OnDefaultBitrateChanged(int value) => HasUnsavedChanges = true;
}

internal class AppSettings
{
    public string DeviceName { get; set; } = "";
    public int HttpPort { get; set; } = 9527;
    public bool AutoStart { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public int ThemeIndex { get; set; } = 2;
    public int DefaultResolution { get; set; } = 0;
    public int DefaultFps { get; set; } = 0;
    public int DefaultBitrate { get; set; } = 1;
}