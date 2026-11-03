using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using UniLinker.Plugin.Sdk;
using UniLinker.WinUI.Services;

namespace UniLinker.WinUI.ViewModels;

public partial class DevicesViewModel : ObservableObject
{
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isScanning = false;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasDevices = false;
    [ObservableProperty] private bool _hasNoDevices = true;

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
        var loc = LocalizationService.Instance;
        HasDevices = Devices.Count > 0;
        HasNoDevices = Devices.Count == 0;
        StatusMessage = Devices.Count > 0
            ? string.Format(loc.DeviceCountFormat, Devices.Count)
            : loc.ScanningForDevices;
    }

    [RelayCommand]
    private void StartScan()
    {
        IsScanning = true;
        StatusMessage = LocalizationService.Instance.ScanningForDevices;

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
    private async Task ConnectManualAsync()
    {
        if (string.IsNullOrEmpty(ManualIp)) return;

        if (int.TryParse(ManualPort, out var port))
        {
            // Probe the device's signaling server and add it to the list.
            StatusMessage = string.Format(LocalizationService.Instance.DeviceCountFormat, Devices.Count);
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var json = await http.GetStringAsync($"http://{ManualIp}:{port}/info");
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var name = doc.RootElement.TryGetProperty("name", out var n)
                    ? n.GetString() : ManualIp;

                var peer = new PeerInfo
                {
                    Id = $"manual-{ManualIp}:{port}",
                    Name = string.IsNullOrEmpty(name) ? ManualIp : name!,
                    IpAddress = ManualIp,
                    Port = port,
                    State = PeerState.Discovered,
                    LastSeen = DateTime.UtcNow,
                };

                if (!Devices.Any(d => d.Id == peer.Id))
                {
                    Devices.Add(peer);
                }
                UpdateStatus();
            }
            catch
            {
                StatusMessage = $"No response from {ManualIp}:{port}";
            }
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