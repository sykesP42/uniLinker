using CommunityToolkit.Mvvm.ComponentModel;

namespace UniLinker.WinUI.Models;

public enum ConnectionQuality
{
    Excellent = 4,
    Good = 3,
    Fair = 2,
    Poor = 1
}

public enum ErrorSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

public partial class ErrorInfo : ObservableObject
{
    public ErrorSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
    public string[] Suggestions { get; init; } = Array.Empty<string>();
    public string? ActionLabel { get; init; }
    public bool IsDismissible { get; init; } = true;
    public int AutoDismissMs { get; init; } = 0;
    public string? ErrorCode { get; init; }
}

public partial class StreamStats : ObservableObject
{
    [ObservableProperty] private int _width;
    [ObservableProperty] private int _height;
    [ObservableProperty] private int _fps;
    [ObservableProperty] private int _bitrateKbps;
    [ObservableProperty] private int _latencyMs;
    [ObservableProperty] private double _packetLossPercent;
    [ObservableProperty] private int _rttMs;
    [ObservableProperty] private ConnectionQuality _quality = ConnectionQuality.Good;

    public string Resolution => $"{Width}x{Height}";
    public string BitrateDisplay => BitrateKbps >= 1000 ? $"{BitrateKbps / 1000.0:F1} Mbps" : $"{BitrateKbps} Kbps";

    public static ConnectionQuality CalculateQuality(int rttMs, double packetLossPercent, int bitrateKbps)
    {
        int score = 4;
        if (rttMs > 100) score--;
        if (rttMs > 200) score--;
        if (packetLossPercent > 1) score--;
        if (packetLossPercent > 5) score--;
        if (bitrateKbps < 2000) score--;
        return (ConnectionQuality)Math.Max(1, Math.Min(4, score));
    }
}
