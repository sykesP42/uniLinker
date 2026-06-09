using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace UniLinker.WinUI.Converters;

/// <summary>
/// Converts between theme index (0, 1, 2) and RadioButton IsChecked boolean
/// Usage: IsChecked="{Binding ThemeIndex, Converter={StaticResource ThemeIndexToBooleanConverter}, ConverterParameter=0, Mode=TwoWay}"
/// </summary>
public class ThemeIndexToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int themeIndex && parameter is string param && int.TryParse(param, out int targetIndex))
        {
            return themeIndex == targetIndex;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isChecked && isChecked && parameter is string param && int.TryParse(param, out int targetIndex))
        {
            return targetIndex;
        }
        return DependencyProperty.UnsetValue;
    }
}
