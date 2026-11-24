using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// An inner shadow overlay, the WPF stand-in for CSS <c>box-shadow: inset 0 y blur color</c>.
/// Place it over a surface in the same grid cell. It draws a blurred ring just outside its
/// bounds and clips to its rounded rectangle, so only the soft inner edge shows.
/// </summary>
public sealed class InsetShadow : FrameworkElement
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(InsetShadow),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(InsetShadow),
        new FrameworkPropertyMetadata(Color.FromArgb(0xB3, 0, 0, 0), OnLookChanged));

    /// <summary>The CSS blur length in pixels.</summary>
    public static readonly DependencyProperty BlurProperty = DependencyProperty.Register(
        nameof(Blur), typeof(double), typeof(InsetShadow),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsArrange, OnLookChanged));

    public static readonly DependencyProperty OffsetYProperty = DependencyProperty.Register(
        nameof(OffsetY), typeof(double), typeof(InsetShadow),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    private readonly Border _ring = new();
    private readonly BlurEffect _blur = new() { KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };

    public InsetShadow()
    {
        IsHitTestVisible = false;
        Focusable = false;
        _ring.Effect = _blur;
        AddVisualChild(_ring);
        UpdateLook();
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public double Blur
    {
        get => (double)GetValue(BlurProperty);
        set => SetValue(BlurProperty, value);
    }

    public double OffsetY
    {
        get => (double)GetValue(OffsetYProperty);
        set => SetValue(OffsetYProperty, value);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _ring;

    protected override Size MeasureOverride(Size availableSize)
    {
        _ring.Measure(new Size(0, 0));
        return new Size(0, 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var t = Math.Max(4, Blur * 1.5) + Math.Abs(OffsetY);
        var r = CornerRadius;
        _ring.BorderThickness = new Thickness(t);
        _ring.CornerRadius = new CornerRadius(r + t);
        _ring.Arrange(new Rect(-t, -t + OffsetY, finalSize.Width + 2 * t, finalSize.Height + 2 * t));

        var clip = new RectangleGeometry(new Rect(finalSize), r, r);
        clip.Freeze();
        Clip = clip;
        return finalSize;
    }

    private static void OnLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((InsetShadow)d).UpdateLook();

    private void UpdateLook()
    {
        var brush = new SolidColorBrush(Color);
        brush.Freeze();
        _ring.BorderBrush = brush;
        _blur.Radius = Blur * 1.5;
    }
}
