using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// A row of LED segments that fills left to right. <see cref="Fraction"/> picks how many of
/// <see cref="Segments"/> light. The segments share the width evenly.
/// </summary>
public class LedBar : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(LedBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnFractionChanged));

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(int), typeof(LedBar),
        new FrameworkPropertyMetadata(30, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(LedBar),
        new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LitBrushProperty = DependencyProperty.Register(
        nameof(LitBrush), typeof(Brush), typeof(LedBar),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnlitBrushProperty = DependencyProperty.Register(
        nameof(UnlitBrush), typeof(Brush), typeof(LedBar),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlowBrushProperty = DependencyProperty.Register(
        nameof(GlowBrush), typeof(Brush), typeof(LedBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    static LedBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(LedBar), new FrameworkPropertyMetadata(typeof(LedBar)));
        HeightProperty.OverrideMetadata(typeof(LedBar), new FrameworkPropertyMetadata(10.0));
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(LedBar), new FrameworkPropertyMetadata(true));
    }

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public int Segments
    {
        get => (int)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
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

    /// <summary>Optional halo behind lit segments. Leave null on light panels.</summary>
    public Brush? GlowBrush
    {
        get => (Brush?)GetValue(GlowBrushProperty);
        set => SetValue(GlowBrushProperty, value);
    }

    public int LitCount => LedMath.LitCount(Fraction, Segments);

    protected override void OnRender(DrawingContext dc)
    {
        var lit = LitCount;
        var h = ActualHeight;
        for (var i = 0; i < Segments; i++)
        {
            var (left, width) = LedMath.SegmentBounds(i, Segments, ActualWidth, Gap);
            var rect = new Rect(left, 0, width, h);
            if (i < lit && GlowBrush is not null)
            {
                var glow = rect;
                glow.Inflate(2, 2);
                dc.DrawRoundedRectangle(GlowBrush, null, glow, 3, 3);
            }

            dc.DrawRoundedRectangle(i < lit ? LitBrush : UnlitBrush, null, rect, 1, 1);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LedBarAutomationPeer(this);

    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (UIElementAutomationPeer.FromElement((UIElement)d) is { } peer)
        {
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, e.OldValue, e.NewValue);
        }
    }

    private sealed class LedBarAutomationPeer(LedBar owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        public bool IsReadOnly => true;
        public double LargeChange => 0;
        public double SmallChange => 0;
        public double Maximum => 1;
        public double Minimum => 0;
        public double Value => ((LedBar)Owner).Fraction;

        public void SetValue(double value) => throw new ElementNotEnabledException();

        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;

        protected override string GetClassNameCore() => nameof(LedBar);
    }
}
