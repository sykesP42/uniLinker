using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace UniLinker.WinUI.Services;

/// <summary>
/// In-app notifications rendered as transient InfoBars over the window
/// content (unpackaged WinUI 3 apps cannot send toast notifications
/// without extra plumbing).
/// </summary>
public class NotificationService
{
    private static NotificationService? _instance;
    public static NotificationService Instance => _instance ??= new NotificationService();

    private DispatcherQueue? _dispatcherQueue;
    private Panel? _host;
    private bool _notificationsEnabled = true;

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => _notificationsEnabled = value;
    }

    public void Initialize(DispatcherQueue dispatcherQueue, Panel notificationHost)
    {
        _dispatcherQueue = dispatcherQueue;
        _host = notificationHost;
    }

    /// <summary>
    /// Show a notification message.
    /// </summary>
    public void ShowToast(string title, string message)
    {
        if (!_notificationsEnabled) return;
        ShowInAppNotification(title, message);
    }

    /// <summary>
    /// Show an in-app InfoBar notification that dismisses itself.
    /// </summary>
    public void ShowInAppNotification(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        var host = _host;
        var dispatcher = _dispatcherQueue;
        if (host == null || dispatcher == null) return;

        dispatcher.TryEnqueue(() =>
        {
            var bar = new InfoBar
            {
                Title = title,
                Message = message,
                Severity = severity,
                IsOpen = true,
                IsClosable = true,
                Margin = new Thickness(0, 0, 0, 8),
                MaxWidth = 380,
                Shadow = SharedShadow,
            };

            bar.Closed += (_, _) => host.Children.Remove(bar);

            // Keep at most 3 notifications visible.
            while (host.Children.Count >= 3)
            {
                host.Children.RemoveAt(0);
            }
            host.Children.Add(bar);

            // Auto dismiss.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                bar.IsOpen = false;
            };
            timer.Start();
        });
    }

    private static Shadow? _sharedShadow;
    private static Shadow SharedShadow => _sharedShadow ??= new ThemeShadow();
}
