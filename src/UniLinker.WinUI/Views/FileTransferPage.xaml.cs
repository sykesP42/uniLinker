using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using UniLinker.WinUI.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace UniLinker.WinUI.Views;

/// <summary>
/// Page for file transfer functionality.
/// Supports drag-and-drop and file selection.
/// </summary>
public sealed partial class FileTransferPage : Page
{
    private MainViewModel? _viewModel;

    public FileTransferPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is MainViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.FileTransfer.SetXamlRoot(this.XamlRoot);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        // Accept file drops
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Drop to send file";
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                if (item is StorageFile file)
                {
                    // Trigger file send via ViewModel
                    if (_viewModel?.FileTransfer.SelectedDevice != null)
                    {
                        // Open file picker instead since we need file path
                        await _viewModel.FileTransfer.SelectFileCommand.ExecuteAsync(null);
                    }
                }
            }
        }
    }
}