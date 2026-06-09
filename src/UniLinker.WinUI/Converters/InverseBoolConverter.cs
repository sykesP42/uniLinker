using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace UniLinker.WinUI.Converters;

/// <summary>
/// Converts boolean to its inverse (true -> false, false -> true)
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return value;
    }
}
