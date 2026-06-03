using CommunityToolkit.Mvvm.ComponentModel;

namespace UniLinker.UI.Models;

/// <summary>
/// 错误信息模型，支持分级错误显示和恢复建议
/// </summary>
public partial class ErrorInfo : ObservableObject
{
    /// <summary>
    /// 错误严重程度
    /// </summary>
    public ErrorSeverity Severity { get; init; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 恢复建议列表
    /// </summary>
    public string[] Suggestions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 操作按钮文本（可选）
    /// </summary>
    public string? ActionLabel { get; init; }

    /// <summary>
    /// 是否可关闭
    /// </summary>
    public bool IsDismissible { get; init; } = true;

    /// <summary>
    /// 自动消失时间（毫秒），0 表示不自动消失
    /// </summary>
    public int AutoDismissMs { get; init; } = 0;

    /// <summary>
    /// 错误码（可选）
    /// </summary>
    public string? ErrorCode { get; init; }
}

/// <summary>
/// 错误严重程度枚举
/// </summary>
public enum ErrorSeverity
{
    /// <summary>
    /// 信息提示 - 蓝色，3秒自动消失
    /// </summary>
    Info,

    /// <summary>
    /// 警告 - 黄色，需手动关闭
    /// </summary>
    Warning,

    /// <summary>
    /// 错误 - 红色，提供解决方案
    /// </summary>
    Error,

    /// <summary>
    /// 严重错误 - 全屏模态对话框，阻止操作
    /// </summary>
    Critical
}

/// <summary>
/// 连接质量等级
/// </summary>
public enum ConnectionQuality
{
    /// <summary>
    /// 优秀 - 4格信号
    /// </summary>
    Excellent = 4,

    /// <summary>
    /// 良好 - 3格信号
    /// </summary>
    Good = 3,

    /// <summary>
    /// 一般 - 2格信号
    /// </summary>
    Fair = 2,

    /// <summary>
    /// 较差 - 1格信号
    /// </summary>
    Poor = 1
}

/// <summary>
/// 实时流统计信息
/// </summary>
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

    /// <summary>
    /// 根据网络指标计算连接质量
    /// </summary>
    public static ConnectionQuality CalculateQuality(int rttMs, double packetLossPercent, int bitrateKbps)
    {
        int score = 4;

        // RTT 评分
        if (rttMs > 100) score--;
        if (rttMs > 200) score--;

        // 丢包率评分
        if (packetLossPercent > 1) score--;
        if (packetLossPercent > 5) score--;

        // 码率评分
        if (bitrateKbps < 2000) score--;

        return (ConnectionQuality)Math.Max(1, Math.Min(4, score));
    }
}
