using System.Windows;
using System.Windows.Media;

namespace ShrinkRay.Theme.Controls;

/// <summary>A slotted panel screw. <see cref="SlotAngle"/> turns the slot so screws do not all match.</summary>
public class Screw : FrameworkElement
{
    public static readonly DependencyProperty SlotAngleProperty = DependencyProperty.Register(
        nameof(SlotAngle), typeof(double), typeof(Screw),
        new FrameworkPropertyMetadata(35.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush Face = MakeFace();
    private static readonly Brush Slot = Decor.Solid(0x5D, 0x56, 0x47);
    private static readonly Brush Shadow = Decor.Solid(0x73, 0, 0, 0);

    static Screw()
    {
        WidthProperty.OverrideMetadata(typeof(Screw), new FrameworkPropertyMetadata(16.0));
        HeightProperty.OverrideMetadata(typeof(Screw), new FrameworkPropertyMetadata(16.0));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(Screw), new UIPropertyMetadata(false));
    }

    /// <summary>Slot angle in degrees.</summary>
    public double SlotAngle
    {
        get => (double)GetValue(SlotAngleProperty);
        set => SetValue(SlotAngleProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var d = System.Math.Min(ActualWidth, ActualHeight);
        var r = d / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(Shadow, null, new Point(c.X, c.Y + 1), r, r);
        dc.DrawEllipse(Face, null, c, r, r);
        dc.PushTransform(new RotateTransform(SlotAngle, c.X, c.Y));
        var inset = d * 3 / 16;
        dc.DrawRectangle(Slot, null, new Rect(c.X - r + inset, c.Y - d / 16, d - 2 * inset, d / 8));
        dc.Pop();
    }

    private static Brush MakeFace()
    {
        var b = new RadialGradientBrush(Color.FromRgb(0xF6, 0xF1, 0xE6), Color.FromRgb(0x8D, 0x84, 0x70))
        {
            GradientOrigin = new Point(0.35, 0.35),
            Center = new Point(0.35, 0.35),
            RadiusX = 0.75,
            RadiusY = 0.75,
        };
        b.Freeze();
        return b;
    }
}

/// <summary>Speaker or vent grille: dark bars at a fixed pitch with a slight inset.</summary>
public class VentGrille : FrameworkElement
{
    public static readonly DependencyProperty BarWidthProperty = DependencyProperty.Register(
        nameof(BarWidth), typeof(double), typeof(VentGrille),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PitchProperty = DependencyProperty.Register(
        nameof(Pitch), typeof(double), typeof(VentGrille),
        new FrameworkPropertyMetadata(9.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(VentGrille),
        new FrameworkPropertyMetadata(Decor.Solid(0x1C, 0x1B, 0x18), FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush TopShade = Decor.Solid(0x40, 0, 0, 0);

    static VentGrille()
    {
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(VentGrille), new FrameworkPropertyMetadata(true));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(VentGrille), new UIPropertyMetadata(false));
    }

    public double BarWidth
    {
        get => (double)GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    public double Pitch
    {
        get => (double)GetValue(PitchProperty);
        set => SetValue(PitchProperty, value);
    }

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Pitch <= 0)
        {
            return;
        }

        var clip = new RectangleGeometry(new Rect(RenderSize), 3, 3);
        dc.PushClip(clip);
        for (var x = 0.0; x < ActualWidth; x += Pitch)
        {
            dc.DrawRectangle(BarBrush, null, new Rect(System.Math.Round(x), 0, BarWidth, ActualHeight));
            dc.DrawRectangle(TopShade, null, new Rect(System.Math.Round(x), 0, BarWidth, 2));
        }

        dc.Pop();
    }
}

/// <summary>Diagonal hazard stripes, for a band that marks something destructive.</summary>
public class HazardStripes : FrameworkElement
{
    public static readonly DependencyProperty StripeBrushProperty = DependencyProperty.Register(
        nameof(StripeBrush), typeof(Brush), typeof(HazardStripes),
        new FrameworkPropertyMetadata(Decor.Solid(0xF0, 0xB4, 0x3C), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
        nameof(Background), typeof(Brush), typeof(HazardStripes),
        new FrameworkPropertyMetadata(Decor.Solid(0x1C, 0x1B, 0x18), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StripeWidthProperty = DependencyProperty.Register(
        nameof(StripeWidth), typeof(double), typeof(HazardStripes),
        new FrameworkPropertyMetadata(7.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(HazardStripes),
        new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    static HazardStripes()
    {
        IsHitTestVisibleProperty.OverrideMetadata(typeof(HazardStripes), new UIPropertyMetadata(false));
    }

    public Brush StripeBrush
    {
        get => (Brush)GetValue(StripeBrushProperty);
        set => SetValue(StripeBrushProperty, value);
    }

    public Brush Background
    {
        get => (Brush)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public double StripeWidth
    {
        get => (double)GetValue(StripeWidthProperty);
        set => SetValue(StripeWidthProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0 || StripeWidth <= 0)
        {
            return;
        }

        var bounds = new Rect(RenderSize);
        dc.PushClip(new RectangleGeometry(bounds, CornerRadius, CornerRadius));
        dc.DrawRectangle(Background, null, bounds);
        var s = StripeWidth;
        for (var x = -h; x < w + h; x += 2 * s)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x, h), true, true);
                ctx.LineTo(new Point(x + h, 0), false, false);
                ctx.LineTo(new Point(x + h + s, 0), false, false);
                ctx.LineTo(new Point(x + s, h), false, false);
            }

            g.Freeze();
            dc.DrawGeometry(StripeBrush, null, g);
        }

        dc.Pop();
    }
}

/// <summary>
/// The 70s spectrum badge: slanted stripes in amber, orange, rust and brick. It sits beside
/// the Michroma wordmark and nowhere else.
/// </summary>
public class StripeBadge : FrameworkElement
{
    public static readonly DependencyProperty StripeCountProperty = DependencyProperty.Register(
        nameof(StripeCount), typeof(int), typeof(StripeBadge),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StripeWidthProperty = DependencyProperty.Register(
        nameof(StripeWidth), typeof(double), typeof(StripeBadge),
        new FrameworkPropertyMetadata(7.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StripeHeightProperty = DependencyProperty.Register(
        nameof(StripeHeight), typeof(double), typeof(StripeBadge),
        new FrameworkPropertyMetadata(28.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(StripeBadge),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The badge colors, warmest last.</summary>
    public static IReadOnlyList<Color> Colors { get; } =
    [
        Color.FromRgb(0xF0, 0xB4, 0x3C),
        Color.FromRgb(0xE2, 0x86, 0x2F),
        Color.FromRgb(0xCF, 0x5A, 0x2A),
        Color.FromRgb(0xA7, 0x3A, 0x22),
    ];

    private static readonly double Lean = System.Math.Tan(20 * System.Math.PI / 180);

    static StripeBadge()
    {
        IsHitTestVisibleProperty.OverrideMetadata(typeof(StripeBadge), new UIPropertyMetadata(false));
    }

    public int StripeCount
    {
        get => (int)GetValue(StripeCountProperty);
        set => SetValue(StripeCountProperty, value);
    }

    public double StripeWidth
    {
        get => (double)GetValue(StripeWidthProperty);
        set => SetValue(StripeWidthProperty, value);
    }

    public double StripeHeight
    {
        get => (double)GetValue(StripeHeightProperty);
        set => SetValue(StripeHeightProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var n = System.Math.Clamp(StripeCount, 1, Colors.Count);
        return new Size(n * StripeWidth + (n - 1) * Gap + Lean * StripeHeight, StripeHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var n = System.Math.Clamp(StripeCount, 1, Colors.Count);
        var h = StripeHeight;
        var shift = Lean * h;
        for (var i = 0; i < n; i++)
        {
            var x = i * (StripeWidth + Gap);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x + shift, 0), true, true);
                ctx.LineTo(new Point(x + shift + StripeWidth, 0), false, false);
                ctx.LineTo(new Point(x + StripeWidth, h), false, false);
                ctx.LineTo(new Point(x, h), false, false);
            }

            g.Freeze();
            var brush = new SolidColorBrush(Colors[i]);
            brush.Freeze();
            dc.DrawGeometry(brush, null, g);
        }
    }
}

internal static class Decor
{
    public static SolidColorBrush Solid(byte r, byte g, byte b) => Solid(0xFF, r, g, b);

    public static SolidColorBrush Solid(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
