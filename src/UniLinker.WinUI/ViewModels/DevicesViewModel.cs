using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using UniLinker.Plugin.Sdk;

namespace UniLinker.WinUI.ViewModels;

public partial class DevicesViewModel : ObservableObject
{
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isScanning = false;
    [ObservableProperty] private string _statusMessage = "Click scan to discover LAN devices";
    [ObservableProperty] private int _discoveryMode = 0; // 0: Auto, 1: LAN, 2: Manual IP

    [ObservableProperty] private string _manualIp = "";
    [ObservableProperty] private string _manualPort = "9527";

    public ObservableCollection<PeerInfo> Devices { get; } = new();

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly IDeviceDiscovery? _discovery;

    public DevicesViewModel()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public DevicesViewModel(IDeviceDiscovery discovery) : this()
    {
        _discovery = discovery;

        // Subscribe to discovery events
        _discovery.DeviceFound += OnDeviceFound;
        _discovery.DeviceLost += OnDeviceLost;

        // Load existing devices
        foreach (var device in _discovery.KnownDevices)
        {
            Devices.Add(device);
        }

        UpdateStatus();
    }

    private void OnDeviceFound(PeerInfo peer)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            // Avoid duplicates
            if (!Devices.Any(d => d.Id == peer.Id))
            {
                Devices.Add(peer);
                UpdateStatus();
            }
        });
    }

    private void OnDeviceLost(PeerInfo peer)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var existing = Devices.FirstOrDefault(d => d.Id == peer.Id);
            if (existing != null)
            {
                Devices.Remove(existing);
                UpdateStatus();
            }
        });
    }

    private void UpdateStatus()
    {
        StatusMessage = Devices.Count > 0
            ? $"{Devices.Count} device(s) discovered"
            : "Scanning for devices...";
    }

    [RelayCommand]
    private void StartScan()
    {
        IsScanning = true;
        StatusMessage = "Scanning LAN...";

        // Discovery is already running in background via Platform.StartAsync()
        // Just show scanning state briefly
        Task.Run(async () =>
        {
            await Task.Delay(2000);
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsScanning = false;
                UpdateStatus();
            });
        });
    }

    [RelayCommand]
    private void ConnectToDevice(PeerInfo? device)
    {
        if (device == null) return;

        // Connection happens via SignalingServer HTTP endpoint
        // User should open browser at http://device.IpAddress:device.Port
        StatusMessage = $"Open browser at http://{device.IpAddress}:{device.Port} to connect";
    }

    [RelayCommand]
    private void ConnectManual()
    {
        if (string.IsNullOrEmpty(ManualIp)) return;

        if (int.TryParse(ManualPort, out var port))
        {
            StatusMessage = $"Open browser at http://{ManualIp}:{port} to connect";
        }
        else
        {
            StatusMessage = "Invalid port number";
        }
    }

    public void Cleanup()
    {
        if (_discovery != null)
        {
            _discovery.DeviceFound -= OnDeviceFound;
            _discovery.DeviceLost -= OnDeviceLost;
        }
    }
}