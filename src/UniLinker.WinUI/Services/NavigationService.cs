using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace UniLinker.WinUI.Services;

public class NavigationService
{
    private Frame? _frame;

    public void Initialize(Frame frame)
    {
        _frame = frame;
    }

    public bool NavigateTo(Type pageType, object? parameter = null)
    {
        if (_frame == null) return false;

        // Don't navigate to the same page
        if (_frame.Content?.GetType() == pageType) return false;

        _frame.Navigate(pageType, parameter, new SlideNavigationTransitionInfo
        {
            Effect = SlideNavigationTransitionEffect.FromRight
        });
        return true;
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
        {
            _frame.GoBack();
        }
    }

    public void ClearHistory()
    {
        _frame?.BackStack.Clear();
    }
}