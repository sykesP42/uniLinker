using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using UniLinker.Plugin.Sdk;
using UniLinker.Plugins.FileTransfer;
using UniLinker.Plugins.FileTransfer.Core;
using UniLinker.WinUI.Services;

namespace UniLinker.WinUI.ViewModels;

/// <summary>
/// Main ViewModel for the File Transfer feature page.
/// Manages the list of transfers, file selection, and device targeting.
/// </summary>
public partial class FileTransferViewModel : ObservableObject
{
    private readonly WebBridge? _bridge;
    private readonly FileTransferPlugin? _plugin;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly IDeviceDiscovery? _discovery;
    private XamlRoot? _xamlRoot;

    #region Observable Properties

    [ObservableProperty] private ObservableCollection<TransferItemViewModel> _transfers = new();
    [ObservableProperty] private bool _isTransferActive;
    [ObservableProperty] private string _saveDirectory = "";
    [ObservableProperty] private PeerInfo? _selectedDevice;
    [ObservableProperty] private int _selectedDeviceIndex = -1;
    [ObservableProperty] private bool _hasDevices;
    [ObservableProperty] private bool _hasTransfers;
    [ObservableProperty] private string _statusMessage = "";

    #endregion

    /// <summary>
    /// List of discovered devices for the device selector.
    /// </summary>
    public ObservableCollection<PeerInfo> Devices { get; } = new();

    /// <summary>
    /// Localization service for UI strings.
    /// </summary>
    public LocalizationService Loc => LocalizationService.Instance;

    public FileTransferViewModel()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        StatusMessage = "Select a device and file to transfer";
    }

    public FileTransferViewModel(WebBridge bridge, IDeviceDiscovery? discovery) : this()
    {
        _bridge = bridge;
        _plugin = bridge.FileTransfer;
        _discovery = discovery;

        // Get save directory
        SaveDirectory = bridge.GetFileTransferSaveDirectory();

        // Subscribe to plugin events
        if (_plugin != null)
        {
            _plugin.TransferStarted += OnTransferStarted;
            _plugin.TransferProgress += OnTransferProgress;
            _plugin.TransferCompleted += OnTransferCompleted;
            _plugin.TransferFailed += OnTransferFailed;
        }

        // Subscribe to discovery events
        if (_discovery != null)
        {
            _discovery.DeviceFound += OnDeviceFound;
            _discovery.DeviceLost += OnDeviceLost;

            // Load existing devices
            foreach (var device in _discovery.KnownDevices)
            {
                Devices.Add(device);
            }
            UpdateHasDevices();
        }
    }

    /// <summary>
    /// Set XamlRoot for dialogs (called from page).
    /// </summary>
    public void SetXamlRoot(XamlRoot root)
    {
        _xamlRoot = root;
    }

    #region Device Discovery Events

    private void OnDeviceFound(PeerInfo peer)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!Devices.Any(d => d.Id == peer.Id))
            {
                Devices.Add(peer);
                UpdateHasDevices();
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
                UpdateHasDevices();
            }
        });
    }

    private void UpdateHasDevices()
    {
        HasDevices = Devices.Count > 0;
        if (!HasDevices)
        {
            StatusMessage = Loc.NoDevicesFound;
        }
    }

    #endregion

    #region File Selection and Sending

    [RelayCommand]
    private async Task SelectFile()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add("*");

            // Get window handle for WinUI 3
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(
                App.Services?.MainWindow ??
                (Microsoft.UI.Xaml.Window.Current ??
                 throw new InvalidOperationException("No window available")));

            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await SendFileAsync(file.Path);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private async Task SendFileAsync(string filePath)
    {
        if (_bridge == null || SelectedDevice == null)
        {
            StatusMessage = "Please select a device first";
            return;
        }

        if (!System.IO.File.Exists(filePath))
        {
            StatusMessage = "File not found";
            return;
        }

        try
        {
            StatusMessage = $"Sending {System.IO.Path.GetFileName(filePath)}...";
            var success = await _bridge.SendFileAsync(SelectedDevice, filePath);

            if (!success)
            {
                StatusMessage = "Failed to start transfer";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    #endregion

    #region Transfer Events

    private void OnTransferStarted(TransferSession session)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var item = new TransferItemViewModel(session, CancelTransferById);
            Transfers.Insert(0, item);
            UpdateHasTransfers();
            IsTransferActive = true;
            StatusMessage = $"Transferring {session.FileName}...";
        });
    }

    private void OnTransferProgress(TransferSession session)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var item = Transfers.FirstOrDefault(t => t.TransferId == session.TransferId);
            item?.UpdateFromSession();
        });
    }

    private void OnTransferCompleted(TransferSession session)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var item = Transfers.FirstOrDefault(t => t.TransferId == session.TransferId);
            if (item != null)
            {
                item.UpdateFromSession();
                StatusMessage = session.IsSender
                    ? $"Sent: {session.FileName}"
                    : $"Received: {session.FileName}";
            }

            CheckIfTransferActive();
        });
    }

    private void OnTransferFailed(TransferSession session, string error)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var item = Transfers.FirstOrDefault(t => t.TransferId == session.TransferId);
            if (item != null)
            {
                item.UpdateFromSession();
                StatusMessage = $"Failed: {session.FileName} - {error}";
            }

            CheckIfTransferActive();
        });
    }

    private void CheckIfTransferActive()
    {
        IsTransferActive = Transfers.Any(t => t.IsTransferring);
    }

    private void UpdateHasTransfers()
    {
        HasTransfers = Transfers.Count > 0;
    }

    #endregion

    #region Commands

    [RelayCommand]
    private void CancelTransfer(string transferId)
    {
        _bridge?.CancelTransfer(transferId);
    }

    private void CancelTransferById(string transferId)
    {
        CancelTransfer(transferId);
    }

    [RelayCommand]
    private void OpenSaveFolder()
    {
        if (string.IsNullOrEmpty(SaveDirectory)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SaveDirectory,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors when opening folder
        }
    }

    [RelayCommand]
    private void ClearCompleted()
    {
        var completed = Transfers.Where(t => t.IsCompleted).ToList();
        foreach (var item in completed)
        {
            item.Cleanup();
            Transfers.Remove(item);
        }
        UpdateHasTransfers();
        CheckIfTransferActive();
    }

    #endregion

    public void Cleanup()
    {
        if (_plugin != null)
        {
            _plugin.TransferStarted -= OnTransferStarted;
            _plugin.TransferProgress -= OnTransferProgress;
            _plugin.TransferCompleted -= OnTransferCompleted;
            _plugin.TransferFailed -= OnTransferFailed;
        }

        if (_discovery != null)
        {
            _discovery.DeviceFound -= OnDeviceFound;
            _discovery.DeviceLost -= OnDeviceLost;
        }

        foreach (var item in Transfers)
        {
            item.Cleanup();
        }
        Transfers.Clear();
    }
}
