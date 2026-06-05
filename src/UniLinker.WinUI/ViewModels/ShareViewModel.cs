using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UniLinker.WinUI.ViewModels;

public partial class ShareViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSharing = false;
    [ObservableProperty] private string _shareAddress = "Waiting to start...";
    [ObservableProperty] private int _viewerCount = 0;
    [ObservableProperty] private string _statusText = "Click start to share your screen";

    [ObservableProperty] private int _resolutionIndex = 0; // 0: 1080p, 1: 2K, 2: 4K
    [ObservableProperty] private int _frameRateIndex = 0; // 0: 30fps, 1: 60fps
    [ObservableProperty] private int _bitrateIndex = 1;   // 0: 10M, 1: 15M, 2: 20M

    public string ResolutionLabel => ResolutionIndex switch
    {
        0 => "1920x1080",
        1 => "2560x1440",
        2 => "3840x2160",
        _ => "1920x1080"
    };

    public int FrameRate => FrameRateIndex == 0 ? 30 : 60;

    public int Bitrate => BitrateIndex switch
    {
        0 => 10000,
        1 => 15000,
        2 => 20000,
        _ => 15000
    };

    [RelayCommand]
    private void StartShare()
    {
        IsSharing = true;
        ShareAddress = "192.168.1.100:9527";
        StatusText = "Sharing screen...";
    }

    [RelayCommand]
    private void StopShare()
    {
        IsSharing = false;
        ShareAddress = "Waiting to start...";
        ViewerCount = 0;
        StatusText = "Share stopped";
    }
}