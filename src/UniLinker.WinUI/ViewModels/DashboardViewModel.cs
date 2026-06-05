using CommunityToolkit.Mvvm.ComponentModel;

namespace UniLinker.WinUI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty] private string _welcomeText = "Welcome to UniLinker";
    [ObservableProperty] private string _subtitle = "Cross-device low-latency screen sharing";

    [ObservableProperty] private int _activeConnections = 0;
    [ObservableProperty] private int _discoveredDevices = 0;
    [ObservableProperty] private string _serverStatus = "Running";
    [ObservableProperty] private bool _isServerRunning = true;
}