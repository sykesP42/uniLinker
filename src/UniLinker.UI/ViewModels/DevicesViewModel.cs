using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UniLinker.Plugin.Sdk;

namespace UniLinker.UI.ViewModels;

public partial class DevicesViewModel : ObservableObject
{
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isScanning = false;
    [ObservableProperty] private string _statusMessage = "点击扫描发现局域网设备";

    public ObservableCollection<PeerInfo> Devices { get; } = new();

    [RelayCommand]
    private void StartScan()
    {
        IsScanning = true;
        StatusMessage = "正在扫描局域网...";

        // Simulate scanning
        Task.Run(async () =>
        {
            await Task.Delay(2000);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsScanning = false;
                StatusMessage = $"扫描完成，发现 {Devices.Count} 台设备";
            });
        });
    }

    [RelayCommand]
    private void ConnectToDevice(PeerInfo? device)
    {
        if (device == null) return;
        StatusMessage = $"正在连接到 {device.Name}...";
    }
}