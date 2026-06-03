using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UniLinker.UI.ViewModels;

public partial class ShareViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSharing = false;
    [ObservableProperty] private string _shareAddress = "等待开始...";
    [ObservableProperty] private int _viewerCount = 0;
    [ObservableProperty] private string _statusText = "点击开始分享您的屏幕";

    [ObservableProperty] private int _resolutionWidth = 1920;
    [ObservableProperty] private int _resolutionHeight = 1080;
    [ObservableProperty] private int _frameRate = 30;
    [ObservableProperty] private int _bitrate = 15000;

    [RelayCommand]
    private void StartShare()
    {
        IsSharing = true;
        ShareAddress = "192.168.1.100:9527";
        StatusText = "正在分享屏幕...";
    }

    [RelayCommand]
    private void StopShare()
    {
        IsSharing = false;
        ShareAddress = "等待开始...";
        ViewerCount = 0;
        StatusText = "分享已停止";
    }
}