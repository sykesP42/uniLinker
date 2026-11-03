using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using UniLinker.Plugin.Sdk;
using UniLinker.WinUI.ViewModels;

namespace UniLinker.WinUI.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is MainViewModel viewModel)
        {
            DataContext = viewModel;
        }
    }

    private async void DeviceView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PeerInfo device } &&
            XamlRoot != null)
        {
            var dialog = new DeviceDetailsDialog(device)
            {
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }
    }
}
