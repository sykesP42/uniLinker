using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using UniLinker.WinUI.Services;

namespace UniLinker.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _deviceName = "UniLinker-PC";
    [ObservableProperty] private int _httpPort = 9527;
    [ObservableProperty] private bool _autoStart = true;
    [ObservableProperty] private bool _minimizeToTray = true;

    [ObservableProperty] private int _defaultResolution = 0;
    [ObservableProperty] private int _defaultFps = 0;
    [ObservableProperty] private int _defaultBitrate = 1;

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
        if (App.Current is App app)
        {
            app.RequestedTheme = index switch
            {
                0 => ApplicationTheme.Dark,
                1 => ApplicationTheme.Light,
                _ => ApplicationTheme.Dark
            };
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var config = new
        {
            DeviceName,
            HttpPort,
            AutoStart,
            MinimizeToTray
        };
        App.Services.Bridge.SaveConfig(System.Text.Json.JsonSerializer.Serialize(config));
    }

    [RelayCommand]
    private void ResetSettings()
    {
        DeviceName = Environment.MachineName;
        HttpPort = 9527;
        AutoStart = true;
        MinimizeToTray = true;
        ThemeIndex = 0;
    }
}