using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using UniLinker.Plugin.Sdk;
using UniLinker.UI.ViewModels;

namespace UniLinker.UI.Views.Pages;

public partial class DevicesView : UserControl
{
    public DevicesView()
    {
        InitializeComponent();
    }

    private void OnDeviceClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is PeerInfo device)
        {
            if (DataContext is DevicesViewModel vm)
            {
                vm.ConnectToDeviceCommand.Execute(device);
            }
        }
    }
}