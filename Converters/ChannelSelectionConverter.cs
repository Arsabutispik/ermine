using Avalonia.Data.Converters;
using Ermine.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ermine.Converters;

public class ChannelSelectionConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values?.Count == 2 && values[0] is Channel currentChannel && values[1] is Channel selectedChannel)
        {
            return currentChannel.Id == selectedChannel.Id;
        }
        
        return false;
    }
}