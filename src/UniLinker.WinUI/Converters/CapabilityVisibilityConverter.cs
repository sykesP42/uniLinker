using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;
using System.Linq;

namespace UniLinker.WinUI.Converters;

public class CapabilityVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string[] capabilities && parameter is string capability)
        {
            return capabilities.Contains(capability)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
