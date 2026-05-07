using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Ermine.Converters;

public class BoolToUnreadBrushConverter : IValueConverter
{
    private readonly IBrush _unreadBrush = SolidColorBrush.Parse("#ffffff");
    private readonly IBrush _readBrush = SolidColorBrush.Parse("#949BA4");  

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool hasUnreads && hasUnreads)
        {
            return _unreadBrush;
        }
        
        return _readBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}