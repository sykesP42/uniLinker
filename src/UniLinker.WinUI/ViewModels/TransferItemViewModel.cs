using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UniLinker.Plugins.FileTransfer.Core;
using UniLinker.Plugins.FileTransfer.Protocol;

namespace UniLinker.WinUI.ViewModels;

/// <summary>
/// ViewModel for a single file transfer item in the UI.
/// Wraps TransferSession and provides formatted display properties.
/// </summary>
public partial class TransferItemViewModel : ObservableObject
{
    private readonly TransferSession _session;
    private readonly Action<string>? _cancelAction;

    #region Read-only Properties

    public string TransferId => _session.TransferId;
    public string FileName => _session.FileName;
    public string FilePath => _session.FilePath;
    public long TotalBytes => _session.TotalBytes;
    public bool IsSender => _session.IsSender;

    public string DirectionIcon => IsSender ? "" : ""; // Upload : Download arrow
    public string DirectionText => IsSender ? "Sending" : "Receiving";

    public string TotalSizeText => FormatSize(TotalBytes);

    #endregion

    #region Observable Properties

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _transferredText = "";
    [ObservableProperty] private string _speedText = "";
    [ObservableProperty] private string _timeText = "";
    [ObservableProperty] private TransferState _state;
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _isTransferring;
    [ObservableProperty] private bool _canCancel;

    #endregion

    public TransferItemViewModel(TransferSession session, Action<string>? cancelAction = null)
    {
        _session = session;
        _cancelAction = cancelAction;

        // Initialize from session
        UpdateFromSession();

        // Subscribe to session events
        _session.ProgressChanged += OnSessionProgressChanged;
        _session.StateChanged += OnSessionStateChanged;
    }

    private void OnSessionProgressChanged(TransferSession session)
    {
        UpdateFromSession();
    }

    private void OnSessionStateChanged(TransferSession session)
    {
        UpdateFromSession();
    }

    public void UpdateFromSession()
    {
        // Progress (0-100)
        Progress = _session.Progress * 100;
        ProgressText = $"{Progress:F0}%";

        // Transferred bytes
        TransferredText = $"{FormatSize(_session.TransferredBytes)} / {FormatSize(TotalBytes)}";

        // Speed
        var bytesPerSecond = _session.BytesPerSecond;
        SpeedText = bytesPerSecond > 0 ? $"{FormatSize((long)bytesPerSecond)}/s" : "";

        // Time elapsed
        var elapsed = _session.Elapsed;
        TimeText = elapsed.TotalSeconds > 0 ? FormatTime(elapsed) : "";

        // State
        State = _session.State;
        StateText = GetStateText(State);
        IsCompleted = State == TransferState.Completed || State == TransferState.Error || State == TransferState.Cancelled;
        IsTransferring = State == TransferState.Transferring;
        CanCancel = !IsCompleted;

        // Error message
        ErrorMessage = _session.ErrorMessage;
    }

    private static string GetStateText(TransferState state) => state switch
    {
        TransferState.Negotiating => "Waiting...",
        TransferState.Transferring => "Transferring",
        TransferState.Completed => "Completed",
        TransferState.Cancelled => "Cancelled",
        TransferState.Error => "Error",
        _ => "Unknown"
    };

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unitIndex = 0;
        double size = bytes;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{size:F0} {units[unitIndex]}" : $"{size:F1} {units[unitIndex]}";
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
            return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
        return $"{time.Minutes:D2}:{time.Seconds:D2}";
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancelAction?.Invoke(TransferId);
    }

    [RelayCommand(CanExecute = nameof(IsCompleted))]
    private void OpenFile()
    {
        if (State == TransferState.Completed && !string.IsNullOrEmpty(FilePath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = FilePath,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore errors when opening file
            }
        }
    }

    [RelayCommand(CanExecute = nameof(IsCompleted))]
    private void OpenFolder()
    {
        if (State == TransferState.Completed && !string.IsNullOrEmpty(FilePath))
        {
            try
            {
                var folder = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(folder))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = folder,
                        UseShellExecute = true
                    });
                }
            }
            catch
            {
                // Ignore errors when opening folder
            }
        }
    }

    public void Cleanup()
    {
        _session.ProgressChanged -= OnSessionProgressChanged;
        _session.StateChanged -= OnSessionStateChanged;
    }
}
