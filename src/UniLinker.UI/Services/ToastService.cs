using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using UniLinker.UI.Controls;

namespace UniLinker.UI.Services;

/// <summary>
/// Toast 通知服务 - 管理 Toast 通知的显示和生命周期
/// </summary>
public class ToastService
{
    private static ToastService? _instance;
    private Panel? _container;
    private readonly DispatcherTimer? _cleanupTimer;

    /// <summary>
    /// 单例实例
    /// </summary>
    public static ToastService Instance => _instance ??= new ToastService();

    private ToastService() { }

    /// <summary>
    /// 初始化 Toast 容器
    /// </summary>
    /// <param name="container">用于承载 Toast 的容器面板</param>
    public void Initialize(Panel container)
    {
        _container = container;
    }

    /// <summary>
    /// 显示成功通知
    /// </summary>
    public void Success(string message, int durationMs = 3000)
    {
        Show(message, ToastType.Success, durationMs);
    }

    /// <summary>
    /// 显示信息通知
    /// </summary>
    public void Info(string message, int durationMs = 3000)
    {
        Show(message, ToastType.Info, durationMs);
    }

    /// <summary>
    /// 显示警告通知
    /// </summary>
    public void Warning(string message, int durationMs = 4000)
    {
        Show(message, ToastType.Warning, durationMs);
    }

    /// <summary>
    /// 显示错误通知
    /// </summary>
    public void Error(string message, int durationMs = 5000)
    {
        Show(message, ToastType.Error, durationMs);
    }

    /// <summary>
    /// 显示 Toast 通知
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <param name="type">通知类型</param>
    /// <param name="durationMs">显示时长（毫秒）</param>
    public void Show(string message, ToastType type, int durationMs = 3000)
    {
        if (_container == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            var toast = new ToastNotification
            {
                Message = message,
                Type = type,
                AutoCloseMs = durationMs,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 20, 20 + _container.Children.Count * 60)
            };

            toast.Closed += (s, e) =>
            {
                // 重新排列剩余的 Toast
                RearrangeToasts();
            };

            _container.Children.Add(toast);
        });
    }

    /// <summary>
    /// 重新排列 Toast 位置
    /// </summary>
    private void RearrangeToasts()
    {
        if (_container == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            int index = 0;
            foreach (var child in _container.Children)
            {
                if (child is ToastNotification toast)
                {
                    toast.Margin = new Thickness(0, 0, 20, 20 + index * 60);
                    index++;
                }
            }
        });
    }

    /// <summary>
    /// 清除所有 Toast
    /// </summary>
    public void ClearAll()
    {
        if (_container == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            _container.Children.Clear();
        });
    }
}
