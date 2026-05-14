using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Ermine.Core; // Ensure this points to where your GlobalCache is!

namespace Ermine.Converters;

public class UserIdToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string authorId)
        {
            return authorId == GlobalCache.CurrentUserId;
        }
        
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("This converter can only be used one way.");
    }
}