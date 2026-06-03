using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace UniLinker.UI.Controls;

/// <summary>
/// Toast 通知类型
/// </summary>
public enum ToastType
{
    Success,
    Info,
    Warning,
    Error
}

/// <summary>
/// Toast 通知组件
/// </summary>
public partial class ToastNotification : UserControl
{
    private Border? _toastCard;
    private TextBlock? _iconText;
    private TextBlock? _messageText;
    private DispatcherTimer? _autoCloseTimer;

    public ToastNotification()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _toastCard = this.FindControl<Border>("ToastCard");
        _iconText = this.FindControl<TextBlock>("IconText");
        _messageText = this.FindControl<TextBlock>("MessageText");
    }

    /// <summary>
    /// 消息文本依赖属性
    /// </summary>
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<ToastNotification, string?>(nameof(Message));

    public string? Message
    {
        get => GetValue(MessageProperty);
        set
        {
            SetValue(MessageProperty, value);
            if (_messageText != null && value != null)
                _messageText.Text = value;
        }
    }

    /// <summary>
    /// Toast 类型依赖属性
    /// </summary>
    public static readonly StyledProperty<ToastType> TypeProperty =
        AvaloniaProperty.Register<ToastNotification, ToastType>(nameof(Type), ToastType.Info);

    public ToastType Type
    {
        get => GetValue(TypeProperty);
        set
        {
            SetValue(TypeProperty, value);
            UpdateStyle(value);
        }
    }

    /// <summary>
    /// 自动关闭时间（毫秒）
    /// </summary>
    public int AutoCloseMs { get; set; } = 3000;

    /// <summary>
    /// 关闭事件
    /// </summary>
    public event EventHandler? Closed;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 设置消息和类型
        if (_messageText != null)
            _messageText.Text = Message ?? "";

        UpdateStyle(Type);

        // 启动自动关闭计时器
        if (AutoCloseMs > 0)
        {
            _autoCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AutoCloseMs)
            };
            _autoCloseTimer.Tick += (s, e) => Close();
            _autoCloseTimer.Start();
        }
    }

    private void UpdateStyle(ToastType type)
    {
        if (_toastCard == null || _iconText == null) return;

        var (bgColor, borderColor, icon, iconColor) = type switch
        {
            ToastType.Success => ("#1A22C55E", "#22C55E", "✓", "#22C55E"),
            ToastType.Info => ("#1A3B82F6", "#3B82F6", "ℹ", "#3B82F6"),
            ToastType.Warning => ("#1AF59E0B", "#F59E0B", "⚠", "#F59E0B"),
            ToastType.Error => ("#1AEF4444", "#EF4444", "✕", "#EF4444"),
            _ => ("#1A6B7280", "#6B7280", "•", "#6B7280")
        };

        _toastCard.Background = Avalonia.Media.Brush.Parse(bgColor);
        _toastCard.BorderBrush = Avalonia.Media.Brush.Parse(borderColor);
        _iconText.Text = icon;
        _iconText.Foreground = Avalonia.Media.Brush.Parse(iconColor);
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    public void Close()
    {
        _autoCloseTimer?.Stop();
        Closed?.Invoke(this, EventArgs.Empty);

        // 从父容器中移除
        if (Parent is Panel panel)
        {
            panel.Children.Remove(this);
        }
    }
}