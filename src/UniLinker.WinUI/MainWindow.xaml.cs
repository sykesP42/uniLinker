using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UniLinker.WinUI.Services;
using UniLinker.WinUI.ViewModels;
using UniLinker.WinUI.Views;

namespace UniLinker.WinUI;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private TrayIcon? _trayIcon;
    private bool _isClosing;
    private bool _minimizeToTray = true;
    private ContentDialog? _closeDialog;

    // Window message for menu commands
    private const int WM_COMMAND = 0x0111;

    public MainWindow()
    {
        InitializeComponent();

        // Set default window size (smaller)
        SetDefaultSize(1024, 680);

        // Initialize ViewModel with real services
        var services = App.Services;
        ViewModel = new MainViewModel(
            services.Bridge,
            services.Platform.Context.Discovery);

        App.Services.Navigation.Initialize(ContentFrame);

        // Navigate to Dashboard on load
        ContentFrame.Navigate(typeof(DashboardPage), ViewModel);

        // Handle pane open/close for status display
        NavView.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, OnPaneStateChanged);
    }

    private void OnPaneStateChanged(DependencyObject sender, DependencyProperty dp)
    {
        var isPaneOpen = NavView.IsPaneOpen;

        // Toggle status visibility
        if (ExpandedStatus != null)
            ExpandedStatus.Visibility = isPaneOpen ? Visibility.Visible : Visibility.Collapsed;

        if (CompactStatus != null)
            CompactStatus.Visibility = isPaneOpen ? Visibility.Collapsed : Visibility.Visible;
    }

    private nint _hwnd;

    private void SetDefaultSize(int width, int height)
    {
        var appWindow = this.AppWindow;
        var size = new Windows.Graphics.SizeInt32(width, height);
        appWindow.Resize(size);

        // Get window handle for tray icon (WinUI 3 way)
        var windowId = this.AppWindow.Id;
        _hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(windowId);

        // Initialize tray icon
        InitializeTray();

        // Center window on screen
        var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(appWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            var centerX = (displayArea.WorkArea.Width - width) / 2;
            var centerY = (displayArea.WorkArea.Height - height) / 2;
            appWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));
        }
    }

    private void InitializeTray()
    {
        _trayIcon = new TrayIcon();
        _trayIcon.Initialize(_hwnd, "UniLinker - Running");

        _trayIcon.ShowClicked += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                this.AppWindow.Show();
                this.Activate();
            });
        };

        _trayIcon.ExitClicked += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _isClosing = true;
                this.Close();
            });
        };
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer == null) return;

        var tag = args.InvokedItemContainer.Tag?.ToString();
        var pageType = tag switch
        {
            "Dashboard" => typeof(DashboardPage),
            "Devices" => typeof(DevicesPage),
            "Share" => typeof(SharePage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage)
        };

        ViewModel.NavigateTo(tag switch
        {
            "Dashboard" => 0,
            "Devices" => 1,
            "Share" => 2,
            "Settings" => 3,
            _ => 0
        });

        ContentFrame.Navigate(pageType, ViewModel);
        NavView.SelectedItem = args.InvokedItemContainer;
    }

    private async void Window_Closed(object sender, WindowEventArgs args)
    {
        // If we're truly closing (from tray exit), just cleanup
        if (_isClosing)
        {
            ViewModel.Cleanup();
            _trayIcon?.Dispose();
            return;
        }

        // Show confirmation dialog
        if (_closeDialog == null)
        {
            args.Handled = true; // Prevent close

            _closeDialog = new ContentDialog
            {
                Title = "Close UniLinker?",
                Content = "Do you want to minimize to system tray (background) or exit completely?",
                PrimaryButtonText = "Minimize to tray",
                SecondaryButtonText = "Exit",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await _closeDialog.ShowAsync();

            _closeDialog = null;

            if (result == ContentDialogResult.Primary)
            {
                // Minimize to tray
                this.AppWindow.Hide();
                _trayIcon?.ShowNotification("UniLinker", "Running in background. Click to restore.");
            }
            else if (result == ContentDialogResult.Secondary)
            {
                // Exit completely
                _isClosing = true;
                ViewModel.Cleanup();
                _trayIcon?.Dispose();
                this.Close();
            }
            // Cancel - do nothing, window stays open
        }
    }
}