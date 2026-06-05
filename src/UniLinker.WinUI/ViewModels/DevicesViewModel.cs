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

    public DevicesViewModel()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    [RelayCommand]
    private void StartScan()
    {
        IsScanning = true;
        StatusMessage = "Scanning LAN...";

        Task.Run(async () =>
        {
            await Task.Delay(2000);
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsScanning = false;
                StatusMessage = $"Scan complete, found {Devices.Count} devices";
            });
        });
    }

    [RelayCommand]
    private void ConnectToDevice(PeerInfo? device)
    {
        if (device == null) return;
        StatusMessage = $"Connecting to {device.Name}...";
    }

    [RelayCommand]
    private void ConnectManual()
    {
        if (string.IsNullOrEmpty(ManualIp)) return;
        StatusMessage = $"Connecting to {ManualIp}:{ManualPort}...";
    }
}