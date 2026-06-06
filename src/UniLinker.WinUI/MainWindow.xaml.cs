using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UniLinker.WinUI.Services;
using UniLinker.WinUI.ViewModels;
using UniLinker.WinUI.Views;

namespace UniLinker.WinUI;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();

        // Initialize ViewModel with real services
        var services = App.Services;
        ViewModel = new MainViewModel(
            services.Bridge,
            services.Platform.Context.Discovery);

        App.Services.Navigation.Initialize(ContentFrame);

        // Navigate to Dashboard on load
        ContentFrame.Navigate(typeof(DashboardPage), ViewModel);
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

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        ViewModel.Cleanup();
    }
}
