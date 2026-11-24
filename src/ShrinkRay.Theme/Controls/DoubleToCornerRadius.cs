using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShrinkRay.Theme.Controls;

/// <summary>Turns a single radius into a uniform <see cref="CornerRadius"/>.</summary>
public sealed class DoubleToCornerRadius : IValueConverter
{
    public static DoubleToCornerRadius Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? new CornerRadius(d) : new CornerRadius(0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is CornerRadius r ? r.TopLeft : 0.0;
}
