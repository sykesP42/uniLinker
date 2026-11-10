using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using System.Text.Json;
using UniLinker.WinUI.Services;

namespace UniLinker.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "UniLinker";

    [ObservableProperty] private string _deviceName = Environment.MachineName;
    [ObservableProperty] private int _httpPort = 9527;
    [ObservableProperty] private bool _autoStart = true;
    [ObservableProperty] private bool _minimizeToTray = true;

    [ObservableProperty] private int _defaultResolution = 0; // 0: 1080p, 1: 2K, 2: 4K
    [ObservableProperty] private int _defaultFps = 0;        // 0: 30fps, 1: 60fps
    [ObservableProperty] private int _defaultBitrate = 1;    // 0: 10M, 1: 15M, 2: 20M

    [ObservableProperty] private int _languageIndex = 0; // 0: English, 1: 中文

    private int _themeIndex = 2; // Default to System
    public int ThemeIndex
    {
        get => _themeIndex;
        set
        {
            if (SetProperty(ref _themeIndex, value))
            {
                MarkDirty();
                ApplyTheme(value);
            }
        }
    }

    private bool _isLoading;
    private bool _loadedOnce;

    public string[] Languages => ["English", "中文"];

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
        _isLoading = true;
        try
        {
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
                    DefaultResolution = settings.DefaultResolution;
                    DefaultFps = settings.DefaultFps;
                    DefaultBitrate = settings.DefaultBitrate;
                    LanguageIndex = settings.LanguageIndex;
                    ThemeIndex = settings.ThemeIndex;
                }
            }

            // Reflect the actual registry autostart state.
            AutoStart = IsRegisteredForAutostart();

            LocalizationService.Instance.SetLanguage(LanguageIndex);
            _loadedOnce = true;
            HasUnsavedChanges = false;
        }
        catch
        {
            // Use defaults if load fails
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void MarkDirty()
    {
        if (!_isLoading && _loadedOnce)
        {
            HasUnsavedChanges = true;
        }
    }

    private void ApplyTheme(int themeIndex)
    {
        var theme = themeIndex switch
        {
            0 => ElementTheme.Dark,
            1 => ElementTheme.Light,
            _ => ElementTheme.Default
        };
        App.CurrentTheme = theme;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
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
                DefaultBitrate = DefaultBitrate,
                LanguageIndex = LanguageIndex
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);

            SetAutostartRegistration(AutoStart);

            // Push device name / port into the running platform (recreates
            // discovery and restarts the signaling server when needed).
            if (_bridge != null)
            {
                await App.ApplyRuntimeSettingsAsync(DeviceName, HttpPort);
            }

            var loc = LocalizationService.Instance;
            StatusMessage = loc.SettingsSaved;
            HasUnsavedChanges = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetSettings()
    {
        _isLoading = true;
        try
        {
            DeviceName = Environment.MachineName;
            HttpPort = 9527;
            AutoStart = true;
            MinimizeToTray = true;
            DefaultResolution = 0;
            DefaultFps = 0;
            DefaultBitrate = 1;
            LanguageIndex = 0;
            ThemeIndex = 2; // System
        }
        finally
        {
            _isLoading = false;
        }

        ApplyTheme(ThemeIndex);
        LocalizationService.Instance.SetLanguage(LanguageIndex);

        StatusMessage = LocalizationService.Instance.SettingsReset;
        HasUnsavedChanges = true;
    }

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasUnsavedChanges = false;

    partial void OnDeviceNameChanged(string value) => MarkDirty();
    partial void OnHttpPortChanged(int value) => MarkDirty();
    partial void OnAutoStartChanged(bool value) => MarkDirty();
    partial void OnMinimizeToTrayChanged(bool value) => MarkDirty();
    partial void OnDefaultResolutionChanged(int value) => MarkDirty();
    partial void OnDefaultFpsChanged(int value) => MarkDirty();
    partial void OnDefaultBitrateChanged(int value) => MarkDirty();
    partial void OnLanguageIndexChanged(int value)
    {
        MarkDirty();
        // Localization applies immediately so the UI reflects the choice.
        LocalizationService.Instance.SetLanguage(value);
    }

    // ── Autostart registry helpers ──

    private static bool IsRegisteredForAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = key?.GetValue(RunValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    private static void SetAutostartRegistration(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key == null) return;

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(RunValueName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Registry access is best effort.
        }
    }
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
    public int LanguageIndex { get; set; } = 0;
}
