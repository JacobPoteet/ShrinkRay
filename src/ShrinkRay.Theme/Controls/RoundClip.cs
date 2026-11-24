using System.Windows;
using System.Windows.Media;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// Clips an element to a rounded rectangle that follows its size. Border alone cannot clip
/// its child to its corners.
/// </summary>
public static class RoundClip
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(double), typeof(RoundClip), new PropertyMetadata(double.NaN, OnRadiusChanged));

    public static double GetRadius(DependencyObject element) => (double)element.GetValue(RadiusProperty);

    public static void SetRadius(DependencyObject element, double value) => element.SetValue(RadiusProperty, value);

    private static void OnRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe)
        {
            return;
        }

        fe.SizeChanged -= OnSizeChanged;
        if (double.IsNaN((double)e.NewValue))
        {
            fe.ClearValue(UIElement.ClipProperty);
            return;
        }

        fe.SizeChanged += OnSizeChanged;
        Update(fe);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Update((FrameworkElement)sender);

    private static void Update(FrameworkElement fe)
    {
        var r = GetRadius(fe);
        var geometry = new RectangleGeometry(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight), r, r);
        geometry.Freeze();
        fe.Clip = geometry;
    }
}
