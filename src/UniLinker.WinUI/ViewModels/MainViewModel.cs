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
    private readonly IDeviceDiscovery? _discovery;
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

    public MainViewModel(WebBridge bridge, IDeviceDiscovery discovery) : this()
    {
        _bridge = bridge;
        _discovery = discovery;

        // Create ViewModels with real services
        Devices = new DevicesViewModel(discovery);
        Share = new ShareViewModel(bridge);
        Settings = new SettingsViewModel(bridge);

        // Subscribe to discovery events for dashboard stats
        _discovery.DeviceFound += OnDeviceFound;
        _discovery.DeviceLost += OnDeviceLost;

        StartRefreshTimer();
    }

    private void OnDeviceFound(PeerInfo peer)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            DeviceCount = _discovery?.KnownDevices.Count ?? 0;
            Dashboard.DiscoveredDevices = DeviceCount;
            UpdateConnectionQuality();
        });
    }

    private void OnDeviceLost(PeerInfo peer)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            DeviceCount = _discovery?.KnownDevices.Count ?? 0;
            Dashboard.DiscoveredDevices = DeviceCount;
            UpdateConnectionQuality();
        });
    }

    private void UpdateConnectionQuality()
    {
        ConnectionQuality = DeviceCount switch
        {
            > 2 => ConnectionQuality.Excellent,
            > 0 => ConnectionQuality.Good,
            _ => ConnectionQuality.Poor
        };
    }

    private void StartRefreshTimer()
    {
        var timer = new System.Timers.Timer(3000);
        timer.Elapsed += (s, e) => _dispatcherQueue.TryEnqueue(UpdateStatus);
        timer.Start();
        UpdateStatus();
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_bridge == null) return;

        try
        {
            var (deviceName, port, peers) = _bridge.GetStatusInfo();
            DeviceInfo = $"{deviceName} | {peers} connections";
            ConnectAddress = $"Listening: 0.0.0.0:{port}";
            Dashboard.ActiveConnections = peers;
            Dashboard.ServerStatus = peers > 0 ? "Active" : "Running";
            Dashboard.IsServerRunning = true;

            DeviceCount = _discovery?.KnownDevices.Count ?? 0;
            Dashboard.DiscoveredDevices = DeviceCount;
            UpdateConnectionQuality();
        }
        catch
        {
            DeviceInfo = "UniLinker";
            Dashboard.ServerStatus = "Error";
            Dashboard.IsServerRunning = false;
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

    public void Cleanup()
    {
        if (_discovery != null)
        {
            _discovery.DeviceFound -= OnDeviceFound;
            _discovery.DeviceLost -= OnDeviceLost;
        }
        Devices.Cleanup();
        Share.Cleanup();
    }
}
