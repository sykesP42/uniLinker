using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia;
using Avalonia.Styling;
using UniLinker.UI;

namespace UniLinker.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _deviceName = "UniLinker-PC";
    [ObservableProperty] private int _httpPort = 9527;
    [ObservableProperty] private bool _autoStart = true;
    [ObservableProperty] private bool _minimizeToTray = true;

    [ObservableProperty] private int _defaultResolution = 0; // 0: 1080p, 1: 2K, 2: 4K
    [ObservableProperty] private int _defaultFps = 1; // 0: 30, 1: 60
    [ObservableProperty] private int _defaultBitrate = 1; // 0: 10M, 1: 15M, 2: 20M

    // Theme: 0 = Dark, 1 = Light, 2 = System
    private int _themeIndex = 0;
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

    private void ApplyTheme(int index)
    {
        if (Application.Current is not App app) return;

        var variant = index switch
        {
            0 => ThemeVariant.Dark,
            1 => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };

        app.SetTheme(variant);
    }
}