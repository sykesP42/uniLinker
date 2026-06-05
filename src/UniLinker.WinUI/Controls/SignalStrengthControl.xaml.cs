using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UniLinker.WinUI.Models;

namespace UniLinker.WinUI.Controls;

public sealed partial class SignalStrengthControl : UserControl
{
    public static readonly DependencyProperty QualityProperty =
        DependencyProperty.Register(nameof(Quality), typeof(ConnectionQuality), typeof(SignalStrengthControl),
            new PropertyMetadata(ConnectionQuality.Good, OnQualityChanged));

    public ConnectionQuality Quality
    {
        get => (ConnectionQuality)GetValue(QualityProperty);
        set => SetValue(QualityProperty, value);
    }

    public SignalStrengthControl()
    {
        InitializeComponent();
        UpdateBars((int)Quality);
    }

    private static void OnQualityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SignalStrengthControl)d;
        control.UpdateBars((int)e.NewValue);
    }

    private void UpdateBars(int quality)
    {
        var activeColor = Application.Current.Resources["SystemAccentColor"] as Microsoft.UI.Xaml.Media.SolidColorBrush;
        var inactiveColor = Application.Current.Resources["TextFillColorTertiaryBrush"] as Microsoft.UI.Xaml.Media.Brush;

        Bar1.Background = quality >= 1 ? activeColor : inactiveColor;
        Bar2.Background = quality >= 2 ? activeColor : inactiveColor;
        Bar3.Background = quality >= 3 ? activeColor : inactiveColor;
        Bar4.Background = quality >= 4 ? activeColor : inactiveColor;
    }
}
