using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// Keeps scientifica crisp on scaled displays. Set <c>PixelText.Size="22"</c> on a TextBlock or
/// panel and the font size snaps to the nearest whole multiple of the font's 11 pixel cell for
/// the monitor's scale. At 100% the size stays 22.
/// </summary>
public static class PixelText
{
    public static readonly DependencyProperty SizeProperty = DependencyProperty.RegisterAttached(
        "Size", typeof(double), typeof(PixelText), new PropertyMetadata(double.NaN, OnSizeChanged));

    public static double GetSize(DependencyObject element) => (double)element.GetValue(SizeProperty);

    public static void SetSize(DependencyObject element, double value) => element.SetValue(SizeProperty, value);

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe)
        {
            return;
        }

        fe.Loaded -= OnLoaded;
        fe.Loaded += OnLoaded;
        Apply(fe);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Apply((FrameworkElement)sender);

    private static void Apply(FrameworkElement fe)
    {
        var desired = GetSize(fe);
        if (double.IsNaN(desired))
        {
            return;
        }

        var snapped = PixelFontMath.Snap(desired, VisualTreeHelper.GetDpi(fe).PixelsPerDip);
        if (fe is TextBlock tb)
        {
            tb.FontSize = snapped;
        }
        else
        {
            fe.SetValue(TextElement.FontSizeProperty, snapped);
        }
    }
}
