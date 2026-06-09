using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using UniLinker.WinUI.ViewModels;

namespace UniLinker.WinUI.Views;

public sealed partial class SharePage : Page
{
    private MainViewModel? _viewModel;
    private Storyboard? _pulseStoryboard;
    private Storyboard? _blinkStoryboard;

    public SharePage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Get the storyboard from resources
        if (Resources.TryGetValue("PulseAnimation", out var resource) && resource is Storyboard storyboard)
        {
            _pulseStoryboard = storyboard;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is MainViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = viewModel;

            // Subscribe to share state changes
            _viewModel.Share.PropertyChanged += OnSharePropertyChanged;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        if (_viewModel != null)
        {
            _viewModel.Share.PropertyChanged -= OnSharePropertyChanged;
        }

        StopAnimations();
    }

    private void OnSharePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShareViewModel.IsSharing))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_viewModel?.Share.IsSharing == true)
                {
                    StartShareAnimations();
                }
                else
                {
                    StopAnimations();
                }
            });
        }
    }

    private void StartShareAnimations()
    {
        // Start pulse animation
        _pulseStoryboard?.Begin();
    }

    private void StopAnimations()
    {
        _pulseStoryboard?.Stop();
        _blinkStoryboard?.Stop();
    }
}