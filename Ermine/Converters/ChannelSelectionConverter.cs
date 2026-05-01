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
        if (values[0] is Channel a && values[1] is Channel b)
            return a.Id == b.Id; 
        return false;
    }
}