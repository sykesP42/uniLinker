using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using UniLinker.Plugin.Sdk;
using System;

namespace UniLinker.WinUI.Converters;

public class PeerStateIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is PeerState state)
        {
            return state switch
            {
                PeerState.Connected => "", // Checkmark icon (green status)
                PeerState.Discovered => "", // Monitor icon
                PeerState.Disconnected => "", // Disconnect icon
                _ => ""
            };
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
