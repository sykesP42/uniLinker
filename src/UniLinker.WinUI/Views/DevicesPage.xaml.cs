using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using UniLinker.Plugin.Sdk;
using UniLinker.WinUI.ViewModels;

namespace UniLinker.WinUI.Views;

public sealed partial class DevicesPage : Page
{
    public DevicesPage()
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

    private async Task ShowDeviceDetailsAsync(PeerInfo device)
    {
        if (XamlRoot == null) return;
        var dialog = new DeviceDetailsDialog(device)
        {
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void DeviceGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PeerInfo device)
        {
            _ = ShowDeviceDetailsAsync(device);
        }
    }

    private void DeviceConnect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PeerInfo device)
        {
            _ = ShowDeviceDetailsAsync(device);
        }
    }
}
