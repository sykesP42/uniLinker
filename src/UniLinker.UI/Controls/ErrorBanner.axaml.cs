using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UniLinker.UI.Models;

namespace UniLinker.UI.Controls;

/// <summary>
/// 错误横幅组件 - 支持分级错误显示和恢复建议
/// </summary>
public partial class ErrorBanner : UserControl
{
    private Border? _mainBorder;
    private TextBlock? _iconText;

    public ErrorBanner()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _mainBorder = this.FindControl<Border>("MainBorder");
        _iconText = this.FindControl<TextBlock>("IconText");
    }

    /// <summary>
    /// 错误信息依赖属性
    /// </summary>
    public static readonly StyledProperty<ErrorInfo?> ErrorProperty =
        AvaloniaProperty.Register<ErrorBanner, ErrorInfo?>(nameof(Error));

    public ErrorInfo? Error
    {
        get => GetValue(ErrorProperty);
        set => SetValue(ErrorProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ErrorProperty)
        {
            UpdateAppearance(change.NewValue as ErrorInfo);
        }
    }

    private void UpdateAppearance(ErrorInfo? error)
    {
        if (error == null || _mainBorder == null || _iconText == null)
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;

        // 根据严重程度设置颜色
        var (bgColor, borderColor, icon) = error.Severity switch
        {
            ErrorSeverity.Info => ("#1A3B82F6", "#3B82F6", "ℹ"),
            ErrorSeverity.Warning => ("#1AF59E0B", "#F59E0B", "⚠"),
            ErrorSeverity.Error => ("#1AEF4444", "#EF4444", "✕"),
            ErrorSeverity.Critical => ("#1AEF4444", "#EF4444", "⛔"),
            _ => ("#1A6B7280", "#6B7280", "?")
        };

        _mainBorder.Background = Avalonia.Media.Brush.Parse(bgColor);
        _mainBorder.BorderBrush = Avalonia.Media.Brush.Parse(borderColor);
        _iconText.Text = icon;
    }

    /// <summary>
    /// 关闭命令
    /// </summary>
    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<ErrorBanner, ICommand?>(nameof(CloseCommand));

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }
}