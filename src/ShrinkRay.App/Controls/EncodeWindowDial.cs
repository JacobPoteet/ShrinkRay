using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;

namespace ShrinkRay.App.Controls;

/// <summary>
/// A 24-hour dial: midnight at the top, the encode window as a lit arc and a hand at the
/// current time. Drawn in the design's 140-unit space and scaled to fit.
/// </summary>
public sealed class EncodeWindowDial : FrameworkElement
{
    public static readonly DependencyProperty StartHoursProperty = Register(nameof(StartHours), 23.0);
    public static readonly DependencyProperty EndHoursProperty = Register(nameof(EndHours), 7.0);
    public static readonly DependencyProperty NowHoursProperty = Register(nameof(NowHours), 0.0);
    public static readonly DependencyProperty IsLitProperty = Register(nameof(IsLit), true);
    public static readonly DependencyProperty ArcBrushProperty = Register<Brush?>(nameof(ArcBrush), null);
    public static readonly DependencyProperty ArcDimBrushProperty = Register<Brush?>(nameof(ArcDimBrush), null);
    public static readonly DependencyProperty GlowBrushProperty = Register<Brush?>(nameof(GlowBrush), null);
    public static readonly DependencyProperty LabelFontProperty = Register(nameof(LabelFont), new FontFamily("Arial Narrow"));

    private const double Size = 140;
    private static readonly Point Center = new(70, 70);
    private static readonly Brush Face = Frozen(Color.FromRgb(0x26, 0x24, 0x21));
    private static readonly Brush Ticks = Frozen(Color.FromRgb(0x8F, 0x86, 0x70));
    private static readonly Brush Label = Frozen(Color.FromRgb(0xCF, 0xC4, 0xAB));
    private static readonly Brush Hand = Frozen(Color.FromRgb(0xEF, 0xE7, 0xD4));

    public double StartHours
    {
        get => (double)GetValue(StartHoursProperty);
        set => SetValue(StartHoursProperty, value);
    }

    public double EndHours
    {
        get => (double)GetValue(EndHoursProperty);
        set => SetValue(EndHoursProperty, value);
    }

    public double NowHours
    {
        get => (double)GetValue(NowHoursProperty);
        set => SetValue(NowHoursProperty, value);
    }

    /// <summary>False draws the window arc unlit, for a window that is turned off.</summary>
    public bool IsLit
    {
        get => (bool)GetValue(IsLitProperty);
        set => SetValue(IsLitProperty, value);
    }

    public Brush? ArcBrush
    {
        get => (Brush?)GetValue(ArcBrushProperty);
        set => SetValue(ArcBrushProperty, value);
    }

    public Brush? ArcDimBrush
    {
        get => (Brush?)GetValue(ArcDimBrushProperty);
        set => SetValue(ArcDimBrushProperty, value);
    }

    public Brush? GlowBrush
    {
        get => (Brush?)GetValue(GlowBrushProperty);
        set => SetValue(GlowBrushProperty, value);
    }

    public FontFamily LabelFont
    {
        get => (FontFamily)GetValue(LabelFontProperty);
        set => SetValue(LabelFontProperty, value);
    }

    /// <summary>The point on the dial for an hour, at a radius in design units.</summary>
    public static Point PointAt(double hours, double radius)
    {
        var angle = (hours / 24 * 360 - 90) * Math.PI / 180;
        return new Point(Center.X + radius * Math.Cos(angle), Center.Y + radius * Math.Sin(angle));
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(Size, availableSize.Width), Math.Min(Size, availableSize.Height));

    protected override void OnRender(DrawingContext dc)
    {
        var scale = Math.Min(ActualWidth, ActualHeight) / Size;
        if (scale <= 0)
        {
            return;
        }

        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawEllipse(Face, null, Center, 66, 66);

        var tick = new Pen(Ticks, 1.5);
        for (var h = 0; h < 24; h++)
        {
            dc.DrawLine(tick, PointAt(h, 59.5), PointAt(h, 64.5));
        }

        var arc = Arc(StartHours, EndHours, 56);
        if (IsLit && GlowBrush is not null)
        {
            dc.DrawGeometry(null, new Pen(GlowBrush, 12) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
        }

        var arcBrush = IsLit ? ArcBrush : ArcDimBrush;
        if (arcBrush is not null)
        {
            dc.DrawGeometry(null, new Pen(arcBrush, 7), arc);
        }

        var typeface = new Typeface(LabelFont, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var h in new[] { 0, 6, 12, 18 })
        {
            var text = new FormattedText(h.ToString("00", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 11, Label, dpi);
            var at = PointAt(h, 32);
            dc.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
        }

        dc.DrawLine(new Pen(Hand, 2.5), Center, PointAt(NowHours, 44));
        dc.DrawEllipse(Hand, null, Center, 5, 5);
        dc.Pop();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    private static Geometry Arc(double start, double end, double radius)
    {
        var span = ((end - start) % 24 + 24) % 24;
        if (span == 0)
        {
            span = 24;
        }

        if (span >= 24)
        {
            return new EllipseGeometry(Center, radius, radius);
        }

        var figure = new PathFigure { StartPoint = PointAt(start, radius), IsClosed = false };
        figure.Segments.Add(new ArcSegment(PointAt(start + span, radius), new Size(radius, radius), 0, span > 12, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static DependencyProperty Register<T>(string name, T value) =>
        DependencyProperty.Register(name, typeof(T), typeof(EncodeWindowDial),
            new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));
}
