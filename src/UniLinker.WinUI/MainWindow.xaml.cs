using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UniLinker.WinUI.Services;
using UniLinker.WinUI.ViewModels;
using UniLinker.WinUI.Views;

namespace UniLinker.WinUI;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private bool _isClosing;
    private bool _minimizeToTray = true;
    private ContentDialog? _closeDialog;

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
    }

    private void SetDefaultSize(int width, int height)
    {
        var appWindow = this.AppWindow;
        var size = new Windows.Graphics.SizeInt32(width, height);
        appWindow.Resize(size);

        // Center window on screen
        var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(appWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        if (displayArea != null)
        {
            var centerX = (displayArea.WorkArea.Width - width) / 2;
            var centerY = (displayArea.WorkArea.Height - height) / 2;
            appWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));
        }
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
        // If we're truly closing, just cleanup
        if (_isClosing)
        {
            ViewModel.Cleanup();
            return;
        }

        // Show confirmation dialog
        if (_closeDialog == null)
        {
            args.Handled = true; // Prevent close

            _closeDialog = new ContentDialog
            {
                Title = "Close UniLinker?",
                Content = "Do you want to minimize to tray (background) or exit completely?",
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
                // Minimize to tray (hide window)
                // Note: Full tray support requires additional implementation
                // For now, just hide the window
                this.AppWindow.Hide();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                // Exit completely
                _isClosing = true;
                ViewModel.Cleanup();
                this.Close();
            }
            // Cancel - do nothing, window stays open
        }
    }
}