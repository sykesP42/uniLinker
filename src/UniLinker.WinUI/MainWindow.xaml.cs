using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using UniLinker.WinUI.Services;
using UniLinker.WinUI.ViewModels;
using UniLinker.WinUI.Views;
using Microsoft.UI;
using Windows.Graphics;

namespace UniLinker.WinUI;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private TrayIcon? _trayIcon;
    private bool _isClosing;
    private ContentDialog? _closeDialog;
    private DispatcherTimer? _shareAnimationTimer;
    private bool _shareAnimationState;

    // Window message for menu commands
    private const int WM_COMMAND = 0x0111;

    public MainWindow()
    {
        InitializeComponent();

        // Configure custom title bar
        ConfigureTitleBar();

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

        // Subscribe to theme changes
        App.ThemeChanged += OnThemeChanged;

        // Subscribe to share state changes
        ViewModel.Share.PropertyChanged += OnShareStateChanged;

        // Initialize notification service
        NotificationService.Instance.Initialize(this.DispatcherQueue, this.Content.XamlRoot);

        // Setup share animation timer
        SetupShareAnimation();
    }

    private void SetupShareAnimation()
    {
        _shareAnimationTimer = new DispatcherTimer();
        _shareAnimationTimer.Interval = TimeSpan.FromMilliseconds(800);
        _shareAnimationTimer.Tick += OnShareAnimationTick;
    }

    private void OnShareAnimationTick(object? sender, object e)
    {
        if (ShareIndicatorTransform != null)
        {
            _shareAnimationState = !_shareAnimationState;
            var scale = _shareAnimationState ? 1.3 : 1.0;

            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                To = scale,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            Storyboard.SetTarget(animation, ShareIndicatorTransform);
            Storyboard.SetTargetProperty(animation, "ScaleX");
            storyboard.Children.Add(animation);

            var animationY = new DoubleAnimation
            {
                To = scale,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            Storyboard.SetTarget(animationY, ShareIndicatorTransform);
            Storyboard.SetTargetProperty(animationY, "ScaleY");
            storyboard.Children.Add(animationY);

            storyboard.Begin();
        }
    }

    private void OnShareStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShareViewModel.IsSharing))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateShareStatus();
            });
        }
    }

    private void UpdateShareStatus()
    {
        var isSharing = ViewModel.Share.IsSharing;
        var isPaneOpen = NavView.IsPaneOpen;

        if (ShareStatusPanel != null)
        {
            ShareStatusPanel.Visibility = isSharing ? Visibility.Visible : Visibility.Collapsed;
        }

        if (ExpandedShareStatus != null)
        {
            ExpandedShareStatus.Visibility = isSharing && isPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        if (CompactShareStatus != null)
        {
            CompactShareStatus.Visibility = isSharing && !isPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        // Start/stop animation
        if (isSharing)
        {
            _shareAnimationTimer?.Start();
        }
        else
        {
            _shareAnimationTimer?.Stop();
            if (ShareIndicatorTransform != null)
            {
                ShareIndicatorTransform.ScaleX = 1.0;
                ShareIndicatorTransform.ScaleY = 1.0;
            }
        }
    }

    private void OnThemeChanged(object? sender, ElementTheme e)
    {
        UpdateTitleBarColors();
    }

    private void OnPaneStateChanged(DependencyObject sender, DependencyProperty dp)
    {
        var isPaneOpen = NavView.IsPaneOpen;

        // Toggle status visibility
        if (ExpandedStatus != null)
            ExpandedStatus.Visibility = isPaneOpen ? Visibility.Visible : Visibility.Collapsed;

        if (CompactStatus != null)
            CompactStatus.Visibility = isPaneOpen ? Visibility.Collapsed : Visibility.Visible;

        // Update share status visibility
        UpdateShareStatus();
    }

    public void UpdateTitleBarForTheme(ElementTheme theme)
    {
        UpdateTitleBarColors();
    }

    private void ConfigureTitleBar()
    {
        // Get the AppWindow
        var appWindow = this.AppWindow;

        // Customize the title bar
        var titleBar = appWindow.TitleBar;

        // Set custom title bar
        titleBar.ExtendsContentIntoTitleBar = true;

        // Set the drag region for the title bar
        if (TitleBarBorder != null)
        {
            // Make the entire title bar draggable
            this.SetTitleBar(TitleBarBorder);
        }

        // Customize title bar colors
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

        // Apply theme colors
        UpdateTitleBarColors();
    }

    private void UpdateTitleBarColors()
    {
        if (this.AppWindow?.TitleBar != null)
        {
            var titleBar = this.AppWindow.TitleBar;

            // Get current theme
            var isDarkTheme = App.CurrentTheme == ElementTheme.Dark ||
                             (App.CurrentTheme == ElementTheme.Default &&
                              Application.Current.RequestedTheme == ApplicationTheme.Dark);

            if (isDarkTheme)
            {
                titleBar.BackgroundColor = Colors.Transparent;
                titleBar.ForegroundColor = Colors.White;
                titleBar.InactiveBackgroundColor = Colors.Transparent;
                titleBar.InactiveForegroundColor = ColorHelper.FromArgb(255, 153, 153, 153);
            }
            else
            {
                titleBar.BackgroundColor = Colors.Transparent;
                titleBar.ForegroundColor = Colors.Black;
                titleBar.InactiveBackgroundColor = Colors.Transparent;
                titleBar.InactiveForegroundColor = ColorHelper.FromArgb(255, 153, 153, 153);
            }
        }
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

            var loc = LocalizationService.Instance;
            _closeDialog = new ContentDialog
            {
                Title = loc.CloseUniLinker,
                Content = loc.MinimizeOrExit,
                PrimaryButtonText = loc.MinimizeToTrayBtn,
                SecondaryButtonText = loc.Exit,
                CloseButtonText = loc.Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await _closeDialog.ShowAsync();

            _closeDialog = null;

            if (result == ContentDialogResult.Primary)
            {
                // Minimize to tray
                this.AppWindow.Hide();
                _trayIcon?.ShowNotification("UniLinker", LocalizationService.Instance.RunningInBackground);
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