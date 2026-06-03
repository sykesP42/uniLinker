using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using UniLinker.UI.ViewModels;

namespace UniLinker.UI.Views.Pages;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void OnStartShareClick(object? sender, RoutedEventArgs e)
    {
        // Find the MainWindow and get its ViewModel
        var mainWindow = this.FindAncestorOfType<MainWindow>();
        if (mainWindow?.DataContext is MainViewModel vm)
        {
            vm.NavigateToCommand.Execute(2);
        }
    }

    private void OnDiscoverClick(object? sender, RoutedEventArgs e)
    {
        var mainWindow = this.FindAncestorOfType<MainWindow>();
        if (mainWindow?.DataContext is MainViewModel vm)
        {
            vm.NavigateToCommand.Execute(1);
        }
    }
}