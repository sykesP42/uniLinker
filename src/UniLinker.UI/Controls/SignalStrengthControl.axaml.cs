using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UniLinker.UI.Models;

namespace UniLinker.UI.Controls;

/// <summary>
/// 信号强度指示器控件 - 四格信号条显示
/// </summary>
public partial class SignalStrengthControl : UserControl
{
    private Border? _bar1, _bar2, _bar3, _bar4;

    public SignalStrengthControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _bar1 = this.FindControl<Border>("Bar1");
        _bar2 = this.FindControl<Border>("Bar2");
        _bar3 = this.FindControl<Border>("Bar3");
        _bar4 = this.FindControl<Border>("Bar4");
    }

    /// <summary>
    /// 连接质量依赖属性
    /// </summary>
    public static readonly StyledProperty<ConnectionQuality> QualityProperty =
        AvaloniaProperty.Register<SignalStrengthControl, ConnectionQuality>(nameof(Quality), ConnectionQuality.Good);

    public ConnectionQuality Quality
    {
        get => GetValue(QualityProperty);
        set => SetValue(QualityProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == QualityProperty)
        {
            UpdateBars((ConnectionQuality)change.NewValue!);
        }
    }

    private void UpdateBars(ConnectionQuality quality)
    {
        if (_bar1 == null || _bar2 == null || _bar3 == null || _bar4 == null) return;

        var activeColor = GetActiveColor(quality);
        var inactiveColor = "#374151";

        int activeCount = (int)quality;

        _bar1.Background = Avalonia.Media.Brush.Parse(activeCount >= 1 ? activeColor : inactiveColor);
        _bar2.Background = Avalonia.Media.Brush.Parse(activeCount >= 2 ? activeColor : inactiveColor);
        _bar3.Background = Avalonia.Media.Brush.Parse(activeCount >= 3 ? activeColor : inactiveColor);
        _bar4.Background = Avalonia.Media.Brush.Parse(activeCount >= 4 ? activeColor : inactiveColor);
    }

    private static string GetActiveColor(ConnectionQuality quality)
    {
        return quality switch
        {
            ConnectionQuality.Excellent => "#22C55E",
            ConnectionQuality.Good => "#84CC16",
            ConnectionQuality.Fair => "#F59E0B",
            ConnectionQuality.Poor => "#EF4444",
            _ => "#6B7280"
        };
    }
}