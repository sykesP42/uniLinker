using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using UniLinker.WinUI.Services;

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

    private readonly WebBridge? _bridge;
    private readonly DispatcherQueue _dispatcherQueue;
    private System.Timers.Timer? _statusTimer;

    public ShareViewModel()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public ShareViewModel(WebBridge bridge) : this()
    {
        _bridge = bridge;
        StartStatusTimer();
    }

    private void StartStatusTimer()
    {
        _statusTimer = new System.Timers.Timer(2000);
        _statusTimer.Elapsed += (s, e) => _dispatcherQueue.TryEnqueue(UpdateStatus);
        _statusTimer.Start();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_bridge == null) return;

        try
        {
            var (_, port, peers) = _bridge.GetStatusInfo();
            ViewerCount = peers;

            if (IsSharing)
            {
                StatusText = peers > 0
                    ? $"Sharing with {peers} viewer(s)"
                    : "Sharing... waiting for viewers";
            }
        }
        catch
        {
            // Ignore errors during status update
        }
    }

    public (int Width, int Height) Resolution => ResolutionIndex switch
    {
        0 => (1920, 1080),
        1 => (2560, 1440),
        2 => (3840, 2160),
        _ => (1920, 1080)
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
        if (_bridge == null)
        {
            StatusText = "Error: Bridge not initialized";
            return;
        }

        var (width, height) = Resolution;
        var config = JsonSerializer.Serialize(new
        {
            width,
            height,
            fps = FrameRate,
            bitrate = Bitrate
        });

        _bridge.StartSharing(config);

        IsSharing = true;

        // Get local IP for display
        var localIp = GetLocalIpAddress();
        var (_, port, _) = _bridge.GetStatusInfo();
        ShareAddress = $"http://{localIp}:{port}";

        StatusText = "Sharing screen...";

        StartStatusTimer();
    }

    [RelayCommand]
    private void StopShare()
    {
        _bridge?.StopSharing();

        IsSharing = false;
        ShareAddress = "Waiting to start...";
        ViewerCount = 0;
        StatusText = "Share stopped";

        _statusTimer?.Stop();
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram,
                System.Net.Sockets.ProtocolType.Udp);

            // Connect to a public DNS server to determine local IP
            socket.Connect("8.8.8.8", 53);
            var endPoint = socket.LocalEndPoint as System.Net.IPEndPoint;
            return endPoint?.Address.ToString() ?? "0.0.0.0";
        }
        catch
        {
            return "0.0.0.0";
        }
    }

    public void Cleanup()
    {
        _statusTimer?.Stop();
        _statusTimer?.Dispose();
    }
}