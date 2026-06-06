using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
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
}
