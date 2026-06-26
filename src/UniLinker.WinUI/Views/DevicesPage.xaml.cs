using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using UniLinker.WinUI.ViewModels;
using UniLinker.WinUI.Views;
using UniLinker.Plugin.Sdk;

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

    private void DiscoveryMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is RadioButtons rb && DataContext is MainViewModel vm)
        {
            if (rb.SelectedItem is RadioButton item && item.Tag is string tag && int.TryParse(tag, out int mode))
            {
                vm.Devices.DiscoveryMode = mode;
            }
        }
    }

    private void DiscoveryModeRadioButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && DataContext is MainViewModel vm)
        {
            if (rb.Tag is string tag && int.TryParse(tag, out int mode))
            {
                vm.Devices.DiscoveryMode = mode;
            }
        }
    }

    private void DeviceGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        // Handle device item click - show details dialog
        if (e.ClickedItem is PeerInfo device)
        {
            var dialog = new DeviceDetailsDialog(device);
            _ = dialog.ShowAsync();
        }
    }

    private void DeviceConnect_Click(object sender, RoutedEventArgs e)
    {
        // Handle Connect button click - show details dialog
        if (sender is Button button && button.Tag is PeerInfo device)
        {
            var dialog = new DeviceDetailsDialog(device);
            _ = dialog.ShowAsync();
        }
    }
}
