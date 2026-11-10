using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using UniLinker.Plugin.Sdk;
using UniLinker.WinUI.Services;

namespace UniLinker.WinUI.ViewModels;

public partial class DevicesViewModel : ObservableObject
{
    /// <summary>
    /// Master device list; Devices is the filtered view bound to the UI.
    /// </summary>
    private readonly List<PeerInfo> _allDevices = new();
    private readonly object _deviceGate = new();

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
        lock (_deviceGate)
        {
            _allDevices.AddRange(_discovery.KnownDevices);
        }
        ApplyFilter();

        UpdateStatus();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void OnDeviceFound(PeerInfo peer)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            lock (_deviceGate)
            {
                // Avoid duplicates
                if (_allDevices.Any(d => d.Id == peer.Id)) return;
                _allDevices.Add(peer);
            }
            ApplyFilter();
            UpdateStatus();
        });
    }

    private void OnDeviceLost(PeerInfo peer)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            lock (_deviceGate)
            {
                var existing = _allDevices.FirstOrDefault(d => d.Id == peer.Id);
                if (existing == null) return;
                _allDevices.Remove(existing);
            }
            ApplyFilter();
            UpdateStatus();
        });
    }

    /// <summary>
    /// Rebuild the visible list from the master list, filtered by the
    /// search text (matches device name or IP address).
    /// </summary>
    private void ApplyFilter()
    {
        List<PeerInfo> filtered;
        lock (_deviceGate)
        {
            filtered = string.IsNullOrWhiteSpace(SearchText)
                ? _allDevices.ToList()
                : _allDevices
                    .Where(d => (d.Name?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false)
                             || (d.IpAddress?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();
        }

        Devices.Clear();
        foreach (var device in filtered)
        {
            Devices.Add(device);
        }
    }

    private void UpdateStatus()
    {
        var loc = LocalizationService.Instance;
        int total;
        lock (_deviceGate)
        {
            total = _allDevices.Count;
        }
        HasDevices = Devices.Count > 0;
        HasNoDevices = Devices.Count == 0;
        StatusMessage = total > 0
            ? string.Format(loc.DeviceCountFormat, total)
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

        if (!int.TryParse(ManualPort, out var port))
        {
            StatusMessage = "Invalid port number";
            return;
        }

        // Probe the device's signaling server and add it to the list.
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

            lock (_deviceGate)
            {
                if (_allDevices.Any(d => d.Id == peer.Id)) return;
                _allDevices.Add(peer);
            }
            ApplyFilter();
            UpdateStatus();
        }
        catch
        {
            StatusMessage = $"No response from {ManualIp}:{port}";
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
