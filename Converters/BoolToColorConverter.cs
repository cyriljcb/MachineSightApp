using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MachineSightApp.Converters;

public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue 
                ? Brushes.Green 
                : Brushes.Red;
        }

        return Brushes.Red;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
