using CommunityToolkit.Mvvm.ComponentModel;

namespace UniLinker.UI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty] private string _welcomeText = "欢迎使用 UniLinker";
    [ObservableProperty] private string _subtitle = "跨设备低延迟投屏解决方案";

    [ObservableProperty] private int _activeConnections = 0;
    [ObservableProperty] private int _discoveredDevices = 0;
    [ObservableProperty] private string _serverStatus = "运行中";
    [ObservableProperty] private bool _isServerRunning = true;
}