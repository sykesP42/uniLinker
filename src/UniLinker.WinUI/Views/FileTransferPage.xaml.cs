using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using UniLinker.WinUI.Services;
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

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        // Accept file drops
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = LocalizationService.Instance.DropFilesHere;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel == null) return;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var item in items)
        {
            if (item is StorageFile file)
            {
                await _viewModel.FileTransfer.SendDroppedFileAsync(file.Path);
            }
        }
    }
}
