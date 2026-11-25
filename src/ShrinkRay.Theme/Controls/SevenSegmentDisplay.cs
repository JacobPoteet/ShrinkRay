using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// A seven-segment readout, drawn rather than set in a font. Shows digits, space, '-', 'S',
/// 'E', ':' and '.'. Unlit segments stay faintly visible, like real glass, and the digits lean
/// 6 degrees. Lit segments are drawn on their own layer, so the glow and bloom come from them alone.
/// </summary>
public class SevenSegmentDisplay : LitReadout
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SevenSegmentDisplay),
        new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnTextChanged));

    public static readonly DependencyProperty DigitHeightProperty = DependencyProperty.Register(
        nameof(DigitHeight), typeof(double), typeof(SevenSegmentDisplay),
        new FrameworkPropertyMetadata(50.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(SevenSegmentDisplay),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SkewProperty = DependencyProperty.Register(
        nameof(Skew), typeof(double), typeof(SevenSegmentDisplay),
        new FrameworkPropertyMetadata(-6.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LitBrushProperty = DependencyProperty.Register(
        nameof(LitBrush), typeof(Brush), typeof(SevenSegmentDisplay),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnlitBrushProperty = DependencyProperty.Register(
        nameof(UnlitBrush), typeof(Brush), typeof(SevenSegmentDisplay),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RollProperty = DependencyProperty.Register(
        nameof(Roll), typeof(CounterRoll), typeof(SevenSegmentDisplay), new PropertyMetadata(null, OnRollChanged));

    private static readonly DependencyProperty RollProgressProperty = DependencyProperty.Register(
        "RollProgress", typeof(double), typeof(SevenSegmentDisplay),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly System.TimeSpan RollTime = System.TimeSpan.FromSeconds(1.6);

    static SevenSegmentDisplay()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SevenSegmentDisplay),
            new FrameworkPropertyMetadata(typeof(SevenSegmentDisplay)));
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(SevenSegmentDisplay), new FrameworkPropertyMetadata(true));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double DigitHeight
    {
        get => (double)GetValue(DigitHeightProperty);
        set => SetValue(DigitHeightProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Lean in degrees, as CSS skewX. Negative leans right.</summary>
    public double Skew
    {
        get => (double)GetValue(SkewProperty);
        set => SetValue(SkewProperty, value);
    }

    public Brush LitBrush
    {
        get => (Brush)GetValue(LitBrushProperty);
        set => SetValue(LitBrushProperty, value);
    }

    public Brush UnlitBrush
    {
        get => (Brush)GetValue(UnlitBrushProperty);
        set => SetValue(UnlitBrushProperty, value);
    }

    /// <summary>Set to count up to <see cref="Text"/> from an earlier reading. Held motion skips the count.</summary>
    public CounterRoll? Roll
    {
        get => (CounterRoll?)GetValue(RollProperty);
        set => SetValue(RollProperty, value);
    }

    /// <summary>What the digits read right now: <see cref="Text"/>, or a step on the way to it during a roll.</summary>
    public string Reading => RollMath.Frame(Roll?.From, Text ?? string.Empty, (double)GetValue(RollProgressProperty));

    private double DigitWidth => DigitHeight * SevenSegmentMath.DigitBoxWidth / SevenSegmentMath.DigitBoxHeight;

    private double MarkSize => System.Math.Max(2, System.Math.Round(DigitHeight * 0.125));

    private double LeanWidth => System.Math.Abs(System.Math.Tan(Skew * System.Math.PI / 180)) * DigitHeight;

    protected override Size MeasureOverride(Size availableSize)
    {
        var cells = SevenSegmentMath.Layout(Text);
        var width = 0.0;
        for (var i = 0; i < cells.Count; i++)
        {
            width += cells[i].Kind == CellKind.Digit ? DigitWidth : MarkSize;
            if (i > 0)
            {
                width += Spacing;
            }
        }

        return new Size(System.Math.Ceiling(width + LeanWidth), System.Math.Ceiling(DigitHeight));
    }

    protected override void Draw(DrawingContext unlit, DrawingContext lit)
    {
        var cells = SevenSegmentMath.Layout(Reading);
        if (cells.Count == 0)
        {
            return;
        }

        var testing = PowerOn.GetIsTesting(this);
        var h = DigitHeight;
        var scale = h / SevenSegmentMath.DigitBoxHeight;
        var lean = LeanWidth;
        // Skewing about the middle pushes one corner lean/2 past the left edge either way.
        foreach (var layer in new[] { unlit, lit })
        {
            layer.PushTransform(new TranslateTransform(lean / 2, 0));
            layer.PushTransform(new SkewTransform(Skew, 0, 0, h / 2));
        }

        var x = 0.0;
        var mark = MarkSize;
        foreach (var cell in cells)
        {
            switch (cell.Kind)
            {
                case CellKind.Digit:
                    DrawDigit(unlit, lit, testing ? Segments.All : cell.Lit, x, scale);
                    x += DigitWidth;
                    break;
                case CellKind.Dot:
                    lit.DrawRectangle(LitBrush, null, new Rect(x, h * 0.96 - mark, mark, mark));
                    x += mark;
                    break;
                case CellKind.Colon:
                    lit.DrawRectangle(LitBrush, null, new Rect(x, h * 0.30 - mark / 2, mark, mark));
                    lit.DrawRectangle(LitBrush, null, new Rect(x, h * 0.70 - mark / 2, mark, mark));
                    x += mark;
                    break;
            }

            x += Spacing;
        }

        foreach (var layer in new[] { unlit, lit })
        {
            layer.Pop();
            layer.Pop();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SevenSegmentAutomationPeer(this);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (UIElementAutomationPeer.FromElement((UIElement)d) is { } peer)
        {
            peer.RaisePropertyChangedEvent(System.Windows.Automation.AutomationElementIdentifiers.NameProperty,
                e.OldValue as string ?? string.Empty, e.NewValue as string ?? string.Empty);
        }
    }

    private static void OnRollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var display = (SevenSegmentDisplay)d;
        display.BeginAnimation(RollProgressProperty, e.NewValue is null || !MotionPolicy.IsMotionEnabled
            ? null
            : new DoubleAnimation(0, 1, RollTime) { FillBehavior = FillBehavior.Stop });
        display.InvalidateVisual();
    }

    private void DrawDigit(DrawingContext unlitLayer, DrawingContext litLayer, Segments lit, double left, double scale)
    {
        foreach (var segment in SevenSegmentMath.EachSegment)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                var points = SevenSegmentMath.Polygon(segment);
                ctx.BeginFigure(new Point(left + points[0].X * scale, points[0].Y * scale), true, true);
                for (var i = 1; i < points.Count; i++)
                {
                    ctx.LineTo(new Point(left + points[i].X * scale, points[i].Y * scale), false, false);
                }
            }

            geometry.Freeze();
            if ((lit & segment) != 0)
            {
                litLayer.DrawGeometry(LitBrush, null, geometry);
            }
            else
            {
                unlitLayer.DrawGeometry(UnlitBrush, null, geometry);
            }
        }
    }

    private sealed class SevenSegmentAutomationPeer(SevenSegmentDisplay owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(SevenSegmentDisplay);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrEmpty(name) ? ((SevenSegmentDisplay)Owner).Text?.Trim() ?? string.Empty : name;
        }
    }
}
