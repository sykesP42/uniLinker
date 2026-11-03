using CommunityToolkit.Mvvm.ComponentModel;

namespace UniLinker.WinUI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty] private int _activeConnections = 0;
    [ObservableProperty] private int _discoveredDevices = 0;
    [ObservableProperty] private string _serverStatus = "Running";
    [ObservableProperty] private bool _isServerRunning = true;
}