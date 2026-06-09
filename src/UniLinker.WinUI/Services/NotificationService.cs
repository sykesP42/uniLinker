using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UniLinker.WinUI.Services;

/// <summary>
/// Service for showing app notifications (toast notifications)
/// Note: For unpackaged WinUI 3 apps, toast notifications require additional setup
/// </summary>
public class NotificationService
{
    private static NotificationService? _instance;
    public static NotificationService Instance => _instance ??= new NotificationService();

    private DispatcherQueue? _dispatcherQueue;
    private XamlRoot? _xamlRoot;
    private bool _notificationsEnabled = true;

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => _notificationsEnabled = value;
    }

    public void Initialize(DispatcherQueue dispatcherQueue, XamlRoot xamlRoot)
    {
        _dispatcherQueue = dispatcherQueue;
        _xamlRoot = xamlRoot;
    }

    /// <summary>
    /// Show a notification message (simplified for unpackaged apps)
    /// </summary>
    public void ShowToast(string title, string message)
    {
        if (!_notificationsEnabled)
            return;

        // For unpackaged WinUI 3 apps, we'll use a simple approach
        // In production, you would use Windows Community Toolkit Notifications
        ShowInAppNotification(title, message);
    }

    /// <summary>
    /// Show an in-app notification using InfoBar
    /// </summary>
    public void ShowInAppNotification(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        if (_dispatcherQueue == null || _xamlRoot == null)
            return;

        _dispatcherQueue.TryEnqueue(() =>
        {
            // This is a simplified approach
            // In production, you would use a proper notification overlay system
        });
    }
}
