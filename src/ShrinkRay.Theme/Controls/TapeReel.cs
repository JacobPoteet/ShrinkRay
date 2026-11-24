using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// One tape reel. <see cref="Pack"/> (0 to 1) sets how much tape is wound on it, so reel size
/// shows file size. Spins counter-clockwise while <see cref="IsSpinning"/>, unless motion is
/// reduced, and holds its angle when stopped.
/// </summary>
public class TapeReel : FrameworkElement
{
    public static readonly DependencyProperty PackProperty = DependencyProperty.Register(
        nameof(Pack), typeof(double), typeof(TapeReel),
        new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsSpinningProperty = DependencyProperty.Register(
        nameof(IsSpinning), typeof(bool), typeof(TapeReel), new PropertyMetadata(false, OnSpinChanged));

    public static readonly DependencyProperty SecondsPerTurnProperty = DependencyProperty.Register(
        nameof(SecondsPerTurn), typeof(double), typeof(TapeReel), new PropertyMetadata(3.2, OnSpinChanged));

    public static readonly DependencyProperty ShowFlangeProperty = DependencyProperty.Register(
        nameof(ShowFlange), typeof(bool), typeof(TapeReel),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PackBrushProperty = DependencyProperty.Register(
        nameof(PackBrush), typeof(Brush), typeof(TapeReel),
        new FrameworkPropertyMetadata(Frozen(Color.FromRgb(0x3A, 0x28, 0x1A)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HubBrushProperty = DependencyProperty.Register(
        nameof(HubBrush), typeof(Brush), typeof(TapeReel),
        new FrameworkPropertyMetadata(Frozen(Color.FromRgb(0xEC, 0xE4, 0xD2)), FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush HoleBrush = Frozen(Color.FromRgb(0x15, 0x14, 0x12));
    private static readonly Brush FlangeFill = Frozen(Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF));
    private static readonly Pen FlangePen = FrozenPen(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), 1.5);
    private static readonly Pen WindPen = FrozenPen(Color.FromArgb(0x0F, 0xFF, 0xDC, 0xB4), 6);

    private readonly RotateTransform _rotation = new();

    static TapeReel()
    {
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(TapeReel), new FrameworkPropertyMetadata(false));
    }

    public TapeReel()
    {
        RenderTransform = _rotation;
        RenderTransformOrigin = new Point(0.5, 0.5);
        Loaded += (_, _) =>
        {
            MotionPolicy.Changed += OnMotionChanged;
            UpdateSpin();
        };
        Unloaded += (_, _) =>
        {
            MotionPolicy.Changed -= OnMotionChanged;
            Hold();
        };
    }

    /// <summary>How full the reel is, 0 to 1.</summary>
    public double Pack
    {
        get => (double)GetValue(PackProperty);
        set => SetValue(PackProperty, value);
    }

    public bool IsSpinning
    {
        get => (bool)GetValue(IsSpinningProperty);
        set => SetValue(IsSpinningProperty, value);
    }

    public double SecondsPerTurn
    {
        get => (double)GetValue(SecondsPerTurnProperty);
        set => SetValue(SecondsPerTurnProperty, value);
    }

    /// <summary>The faint outer flange ring.</summary>
    public bool ShowFlange
    {
        get => (bool)GetValue(ShowFlangeProperty);
        set => SetValue(ShowFlangeProperty, value);
    }

    public Brush PackBrush
    {
        get => (Brush)GetValue(PackBrushProperty);
        set => SetValue(PackBrushProperty, value);
    }

    public Brush HubBrush
    {
        get => (Brush)GetValue(HubBrushProperty);
        set => SetValue(HubBrushProperty, value);
    }

    /// <summary>The current angle, including any running spin.</summary>
    public double Angle => _rotation.Angle;

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width) ? 168 : availableSize.Width;
        var h = double.IsInfinity(availableSize.Height) ? 168 : availableSize.Height;
        var d = System.Math.Min(w, h);
        return new Size(d, d);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var d = System.Math.Min(ActualWidth, ActualHeight);
        if (d <= 0)
        {
            return;
        }

        var s = ReelMath.Scale(d);
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        if (ShowFlange)
        {
            dc.DrawEllipse(FlangeFill, FlangePen, c, ReelMath.FlangeRadius * s, ReelMath.FlangeRadius * s);
        }

        var pack = ReelMath.PackRadius(Pack);
        dc.DrawEllipse(PackBrush, null, c, pack * s, pack * s);
        if (pack - 9 > ReelMath.HubRadius + 1)
        {
            dc.DrawEllipse(null, ScaledPen(WindPen, s), c, (pack - 9) * s, (pack - 9) * s);
        }

        dc.DrawEllipse(HubBrush, null, c, ReelMath.HubRadius * s, ReelMath.HubRadius * s);
        for (var i = 0; i < 3; i++)
        {
            var a = i * 2 * System.Math.PI / 3;
            var p = new Point(c.X + 17 * s * System.Math.Sin(a), c.Y - 17 * s * System.Math.Cos(a));
            dc.DrawEllipse(HoleBrush, null, p, 7 * s, 7 * s);
        }

        dc.DrawEllipse(HoleBrush, null, c, 8 * s, 8 * s);
    }

    private static void OnSpinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((TapeReel)d).UpdateSpin();

    private void OnMotionChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateSpin);

    private void UpdateSpin()
    {
        Hold();
        if (!IsSpinning || !IsLoaded || !MotionPolicy.IsMotionEnabled || SecondsPerTurn <= 0)
        {
            return;
        }

        var from = _rotation.Angle;
        var spin = new DoubleAnimation(from, from - 360, TimeSpan.FromSeconds(SecondsPerTurn))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        _rotation.BeginAnimation(RotateTransform.AngleProperty, spin);
    }

    private void Hold()
    {
        var angle = _rotation.Angle % 360;
        _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        _rotation.Angle = angle;
    }

    private static Pen ScaledPen(Pen pen, double s)
    {
        var scaled = new Pen(pen.Brush, pen.Thickness * s);
        scaled.Freeze();
        return scaled;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var p = new Pen(Frozen(color), thickness);
        p.Freeze();
        return p;
    }
}
