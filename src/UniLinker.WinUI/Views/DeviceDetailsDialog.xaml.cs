using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UniLinker.Plugin.Sdk;

namespace UniLinker.WinUI.Views;

public sealed partial class DeviceDetailsDialog : ContentDialog
{
    private readonly PeerInfo _device;

    public DeviceDetailsDialog(PeerInfo device)
    {
        InitializeComponent();
        _device = device;
        DataContext = device;

        // Set dialog size
        Width = 800;
        Height = 600;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Hide loading indicator when WebView2 is ready
        WebView.NavigationCompleted += OnNavigationCompleted;

        try
        {
            // Initialize WebView2
            await WebView.EnsureCoreWebView2Async();

            // Construct URL from device info
            var url = $"http://{_device.IpAddress}:{_device.Port}";
            WebView.Source = new Uri(url);
        }
        catch (Exception ex)
        {
            // WebView2 initialization failed
            LoadingIndicator.IsActive = false;

            // Show error message in WebView area
            var errorGrid = new Grid();
            errorGrid.Children.Add(new TextBlock
            {
                Text = "WebView2 initialization failed. Please ensure WebView2 Runtime is installed.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.WrapWholeWords
            });

            // Replace WebView with error message
            var parent = WebView.Parent as Grid;
            if (parent != null)
            {
                parent.Children.Remove(WebView);
                parent.Children.Add(errorGrid);
            }
        }
    }

    private void OnNavigationCompleted(WebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
    {
        // Hide loading indicator after navigation
        LoadingIndicator.IsActive = false;

        // Check for navigation failure
        if (!args.IsSuccess)
        {
            LoadingIndicator.IsActive = false;

            // Show connection error
            var errorText = new TextBlock
            {
                Text = $"Cannot connect to device at {_device.IpAddress}:{_device.Port}. Please check network connectivity.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.WrapWholeWords,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorCriticalBrush"]
            };

            var parent = WebView.Parent as Grid;
            if (parent != null)
            {
                parent.Children.Clear();
                parent.Children.Add(errorText);
            }
        }
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        // Clean up WebView2 resources
        WebView.NavigationCompleted -= OnNavigationCompleted;
        WebView.Close();
    }
}