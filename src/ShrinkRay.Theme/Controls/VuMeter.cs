using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// A moving-needle meter on a paper face, with a red zone at the top of the scale. The needle
/// swings to each new <see cref="Value"/> with a small overshoot and settles.
/// </summary>
[TemplatePart(Name = "PART_Needle", Type = typeof(UIElement))]
[TemplatePart(Name = "PART_RedZone", Type = typeof(System.Windows.Shapes.Path))]
public class VuMeter : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(VuMeter), new PropertyMetadata(0.0, OnReadingChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(VuMeter), new PropertyMetadata(0.0, OnReadingChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(VuMeter), new PropertyMetadata(100.0, OnReadingChanged));

    /// <summary>Where the red zone starts, as a fraction of the scale.</summary>
    public static readonly DependencyProperty RedZoneStartProperty = DependencyProperty.Register(
        nameof(RedZoneStart), typeof(double), typeof(VuMeter), new PropertyMetadata(0.75, OnRedZoneChanged));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(VuMeter), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MinLabelProperty = DependencyProperty.Register(
        nameof(MinLabel), typeof(string), typeof(VuMeter), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MidLabelProperty = DependencyProperty.Register(
        nameof(MidLabel), typeof(string), typeof(VuMeter), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MaxLabelProperty = DependencyProperty.Register(
        nameof(MaxLabel), typeof(string), typeof(VuMeter), new PropertyMetadata(string.Empty));

    private static readonly Duration Swing = new(TimeSpan.FromMilliseconds(300));
    private RotateTransform? _needle;

    static VuMeter()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(VuMeter), new FrameworkPropertyMetadata(typeof(VuMeter)));
        FocusableProperty.OverrideMetadata(typeof(VuMeter), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(VuMeter), new FrameworkPropertyMetadata(false));
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double RedZoneStart
    {
        get => (double)GetValue(RedZoneStartProperty);
        set => SetValue(RedZoneStartProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public string MinLabel
    {
        get => (string)GetValue(MinLabelProperty);
        set => SetValue(MinLabelProperty, value);
    }

    public string MidLabel
    {
        get => (string)GetValue(MidLabelProperty);
        set => SetValue(MidLabelProperty, value);
    }

    public string MaxLabel
    {
        get => (string)GetValue(MaxLabelProperty);
        set => SetValue(MaxLabelProperty, value);
    }

    /// <summary>The angle the needle is heading for: its reading, or full scale during the self-test.</summary>
    public double TargetAngle => VuMath.NeedleAngle(PowerOn.GetIsTesting(this) ? Maximum : Value, Minimum, Maximum);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _needle = null;
        if (GetTemplateChild("PART_Needle") is UIElement needle)
        {
            _needle = new RotateTransform(TargetAngle, VuMath.Pivot.X, VuMath.Pivot.Y);
            needle.RenderTransform = _needle;
        }

        UpdateRedZone();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == PowerOn.IsTestingProperty)
        {
            SwingToTarget();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new VuMeterAutomationPeer(this);

    private static void OnReadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var meter = (VuMeter)d;
        meter.SwingToTarget();
        if (e.Property == ValueProperty && UIElementAutomationPeer.FromElement(meter) is { } peer)
        {
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, e.OldValue, e.NewValue);
        }
    }

    private void SwingToTarget() => _needle?.BeginAnimation(RotateTransform.AngleProperty,
        new DoubleAnimation(TargetAngle, Swing) { EasingFunction = CubicBezierEase.Needle });

    private static void OnRedZoneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((VuMeter)d).UpdateRedZone();

    private void UpdateRedZone()
    {
        if (GetTemplateChild("PART_RedZone") is not System.Windows.Shapes.Path path)
        {
            return;
        }

        var start = VuMath.PointAt(-VuMath.Sweep + System.Math.Clamp(RedZoneStart, 0, 1) * 2 * VuMath.Sweep, VuMath.ScaleRadius);
        var end = VuMath.PointAt(VuMath.Sweep, VuMath.ScaleRadius);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(VuMath.ScaleRadius, VuMath.ScaleRadius), 0, false, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        path.Data = geometry;
    }

    private sealed class VuMeterAutomationPeer(VuMeter owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        private VuMeter Meter => (VuMeter)Owner;

        public bool IsReadOnly => true;
        public double LargeChange => 0;
        public double SmallChange => 0;
        public double Maximum => Meter.Maximum;
        public double Minimum => Meter.Minimum;
        public double Value => Meter.Value;

        public void SetValue(double value) => throw new ElementNotEnabledException();

        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;

        protected override string GetClassNameCore() => nameof(VuMeter);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrEmpty(name) ? Meter.Caption ?? string.Empty : name;
        }
    }
}
