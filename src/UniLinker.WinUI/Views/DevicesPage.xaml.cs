using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
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
        // Handle device item click
        if (e.ClickedItem != null)
        {
            // TODO: Show device details or connect
        }
    }

    private void DeviceConnect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag != null)
        {
            // TODO: Connect to device
            // var device = button.Tag as Device;
        }
    }
}
