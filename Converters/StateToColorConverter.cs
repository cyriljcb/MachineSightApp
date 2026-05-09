using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MachineSightApp.Models;

namespace MachineSightApp.Converters;

public class StateToColorConverter : IValueConverter
{
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (SensorState)value switch {
            SensorState.Ok     => Brushes.Green,
            SensorState.Warn   => Brushes.Yellow,
            SensorState.Danger => Brushes.Red,
            _                  => Brushes.Gray
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
