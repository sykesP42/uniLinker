using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UniLinker.Core;
using UniLinker.Plugin.Sdk;
using UniLinker.UI.Models;

namespace UniLinker.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly WebBridge? _bridge;
    private readonly Platform? _platform;

    [ObservableProperty] private bool _isSharing;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private string _deviceInfo = "本机: UniLinker";
    [ObservableProperty] private string _connectAddress = "监听: 0.0.0.0:9527";
    [ObservableProperty] private PeerInfo? _selectedDevice;
    [ObservableProperty] private int _deviceCount;
    [ObservableProperty] private int _currentNavIndex = 0;

    // 连接质量 (用于信号指示器)
    [ObservableProperty] private ConnectionQuality _connectionQuality = ConnectionQuality.Good;

    // Page ViewModels
    public DashboardViewModel Dashboard { get; } = new();
    public DevicesViewModel Devices { get; } = new();
    public ShareViewModel Share { get; } = new();
    public SettingsViewModel Settings { get; } = new();

    // Current page for navigation
    [ObservableProperty] private ObservableObject _currentPage;

    public MainViewModel()
    {
        _currentPage = Dashboard;
    }

    public MainViewModel(WebBridge bridge) : this()
    {
        _bridge = bridge;
        _platform = bridge.Platform;
        StartRefreshTimer();
    }

    private void StartRefreshTimer()
    {
        var timer = new System.Timers.Timer(3000);
        timer.Elapsed += (s, e) => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshDevices);
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
            StatusText = "搜索中...";
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
            StatusText = "就绪";

            // 根据连接数估算质量
            ConnectionQuality = DeviceCount > 0 ? ConnectionQuality.Excellent : ConnectionQuality.Fair;
        }

        UpdateDeviceInfo();
    }

    private void UpdateDeviceInfo()
    {
        if (_platform == null) return;

        try
        {
            var config = _platform.Context.Config.Get<PlatformConfig>("platform");
            var port = config.HttpPort > 0 ? config.HttpPort : 9527;
            var peers = _platform.PeerMesh?.ConnectedPeers.Count ?? 0;

            DeviceInfo = $"{config.DeviceName} | {peers} 连接";
            ConnectAddress = $"监听: 0.0.0.0:{port}";
            Dashboard.ActiveConnections = peers;
        }
        catch
        {
            DeviceInfo = "UniLinker";
        }
    }

    [RelayCommand]
    private void NavigateTo(int index)
    {
        CurrentNavIndex = index;
        CurrentPage = index switch
        {
            0 => Dashboard,
            1 => Devices,
            2 => Share,
            3 => Settings,
            _ => Dashboard
        };

        // Update status text based on page
        StatusText = index switch
        {
            0 => "仪表盘",
            1 => "设备发现",
            2 => "屏幕分享",
            3 => "设置",
            _ => "就绪"
        };
    }

    [RelayCommand]
    private void StartShare()
    {
        _bridge?.StartSharing(System.Text.Json.JsonSerializer.Serialize(new
        {
            width = 1920,
            height = 1080,
            fps = 30,
            bitrate = 15000,
        }));

        IsSharing = true;
        Share.IsSharing = true;
        StatusText = "正在分享";
    }

    [RelayCommand]
    private void StopShare()
    {
        _bridge?.StopSharing();
        IsSharing = false;
        Share.IsSharing = false;
        StatusText = "就绪";
    }

    public void WatchDevice(PeerInfo device)
    {
        _bridge?.WatchDevice(device.Id);
        StatusText = $"已连接到 {device.Name}";
    }
}