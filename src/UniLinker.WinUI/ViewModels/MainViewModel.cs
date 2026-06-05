using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using UniLinker.Plugin.Sdk;
using UniLinker.WinUI.Models;
using UniLinker.WinUI.Services;

namespace UniLinker.WinUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly WebBridge? _bridge;
    private readonly DispatcherQueue _dispatcherQueue;

    [ObservableProperty] private int _currentNavIndex = 0;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _deviceInfo = "Local: UniLinker";
    [ObservableProperty] private string _connectAddress = "Listening: 0.0.0.0:9527";
    [ObservableProperty] private int _deviceCount;
    [ObservableProperty] private ConnectionQuality _connectionQuality = ConnectionQuality.Good;

    public DashboardViewModel Dashboard { get; }
    public DevicesViewModel Devices { get; }
    public ShareViewModel Share { get; }
    public SettingsViewModel Settings { get; }

    public MainViewModel()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        Dashboard = new();
        Devices = new();
        Share = new();
        Settings = new();
    }

    public MainViewModel(WebBridge bridge) : this()
    {
        _bridge = bridge;
        StartRefreshTimer();
    }

    private void StartRefreshTimer()
    {
        var timer = new System.Timers.Timer(3000);
        timer.Elapsed += (s, e) => _dispatcherQueue.TryEnqueue(RefreshDevices);
        timer.Start();
        RefreshDevices();
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        Devices.Devices.Clear();

        var discovered = _bridge?.GetDiscoveredDevices();
        if (discovered == null || discovered.Count == 0)
        {
            StatusText = "Searching...";
            DeviceCount = 0;
            Dashboard.DiscoveredDevices = 0;
            ConnectionQuality = ConnectionQuality.Poor;
        }
        else
        {
            foreach (var device in discovered)
            {
                Devices.Devices.Add(device);
            }
            DeviceCount = discovered.Count;
            Dashboard.DiscoveredDevices = discovered.Count;
            StatusText = "Ready";
            ConnectionQuality = DeviceCount > 0 ? ConnectionQuality.Excellent : ConnectionQuality.Fair;
        }

        UpdateDeviceInfo();
    }

    private void UpdateDeviceInfo()
    {
        if (_bridge == null) return;

        try
        {
            var (deviceName, port, peers) = _bridge.GetStatusInfo();
            DeviceInfo = $"{deviceName} | {peers} connections";
            ConnectAddress = $"Listening: 0.0.0.0:{port}";
            Dashboard.ActiveConnections = peers;
            Dashboard.ServerStatus = peers > 0 ? "Active" : "Running";
        }
        catch
        {
            DeviceInfo = "UniLinker";
        }
    }

    public void NavigateTo(int index)
    {
        CurrentNavIndex = index;
        StatusText = index switch
        {
            0 => "Dashboard",
            1 => "Devices",
            2 => "Share",
            3 => "Settings",
            _ => "Ready"
        };
    }
}