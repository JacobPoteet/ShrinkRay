using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// A detented rotary knob, such as the quality (CQ) selector. A click steps one detent
/// clockwise and wraps; a right click steps back. Arrow keys and the wheel step and stop at
/// the ends; Home and End jump there. It is a real button for automation and reports the
/// selected detent as its value.
/// </summary>
[TemplatePart(Name = "PART_Pointer", Type = typeof(UIElement))]
[TemplatePart(Name = "PART_Labels", Type = typeof(Canvas))]
public class RotaryKnob : ButtonBase
{
    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IList<string>), typeof(RotaryKnob),
        new PropertyMetadata(new ReadOnlyCollection<string>([]), OnLabelsChanged));

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(RotaryKnob),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedIndexChanged, CoerceSelectedIndex));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(RotaryKnob), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(RotaryKnob), new PropertyMetadata(Brushes.Black, OnLabelLookChanged));

    public static readonly DependencyProperty SelectedLabelBrushProperty = DependencyProperty.Register(
        nameof(SelectedLabelBrush), typeof(Brush), typeof(RotaryKnob), new PropertyMetadata(Brushes.Red, OnLabelLookChanged));

    private static readonly Duration Turn = new(TimeSpan.FromMilliseconds(180));
    private RotateTransform? _pointer;
    private readonly List<TextBlock> _labelBlocks = [];

    static RotaryKnob()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RotaryKnob), new FrameworkPropertyMetadata(typeof(RotaryKnob)));
    }

    /// <summary>The detent labels, clockwise. In XAML: <c>Labels="20,22,24,26,28,30"</c>.</summary>
    [TypeConverter(typeof(CommaListConverter))]
    public IList<string> Labels
    {
        get => (IList<string>)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>What the knob sets, such as "Quality CQ". Used for the automation name.</summary>
    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public Brush SelectedLabelBrush
    {
        get => (Brush)GetValue(SelectedLabelBrushProperty);
        set => SetValue(SelectedLabelBrushProperty, value);
    }

    public string SelectedLabel =>
        SelectedIndex >= 0 && SelectedIndex < Labels.Count ? Labels[SelectedIndex] : string.Empty;

    public double PointerAngle => KnobMath.DetentAngle(System.Math.Max(0, SelectedIndex), System.Math.Max(1, Labels.Count));

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _pointer = null;
        if (GetTemplateChild("PART_Pointer") is UIElement pointer)
        {
            // The transform works in the pointer's own coordinates, so offset the knob center
            // by where the pointer sits on the canvas.
            var left = Canvas.GetLeft(pointer);
            var top = Canvas.GetTop(pointer);
            _pointer = new RotateTransform(PointerAngle,
                KnobMath.Center.X - (double.IsNaN(left) ? 0 : left),
                KnobMath.Center.Y - (double.IsNaN(top) ? 0 : top));
            pointer.RenderTransform = _pointer;
        }

        BuildLabels();
    }

    /// <summary>Moves one detent. Positive is clockwise.</summary>
    public void Step(int delta, bool wrap) =>
        SetCurrentValue(SelectedIndexProperty, KnobMath.Step(SelectedIndex, Labels.Count, delta, wrap));

    protected override void OnClick()
    {
        Step(+1, wrap: true);
        base.OnClick();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        Focus();
        Step(-1, wrap: true);
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Step(e.Delta > 0 ? +1 : -1, wrap: false);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right:
            case Key.Up:
                Step(+1, wrap: false);
                e.Handled = true;
                return;
            case Key.Left:
            case Key.Down:
                Step(-1, wrap: false);
                e.Handled = true;
                return;
            case Key.Home:
                SetCurrentValue(SelectedIndexProperty, 0);
                e.Handled = true;
                return;
            case Key.End:
                SetCurrentValue(SelectedIndexProperty, Labels.Count - 1);
                e.Handled = true;
                return;
        }

        base.OnKeyDown(e);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RotaryKnobAutomationPeer(this);

    private static object CoerceSelectedIndex(DependencyObject d, object value)
    {
        var count = ((RotaryKnob)d).Labels?.Count ?? 0;
        return count == 0 ? 0 : System.Math.Clamp((int)value, 0, count - 1);
    }

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var knob = (RotaryKnob)d;
        knob._pointer?.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(knob.PointerAngle, Turn) { EasingFunction = CubicBezierEase.Knob });
        knob.ColorLabels();
        if (UIElementAutomationPeer.FromElement(knob) is { } peer)
        {
            peer.RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, string.Empty, knob.SelectedLabel);
        }
    }

    private static void OnLabelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var knob = (RotaryKnob)d;
        knob.CoerceValue(SelectedIndexProperty);
        knob.BuildLabels();
        if (knob._pointer is { } pointer)
        {
            pointer.BeginAnimation(RotateTransform.AngleProperty, null);
            pointer.Angle = knob.PointerAngle;
        }
    }

    private static void OnLabelLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((RotaryKnob)d).ColorLabels();

    private void BuildLabels()
    {
        if (GetTemplateChild("PART_Labels") is not Canvas canvas)
        {
            return;
        }

        canvas.Children.Clear();
        _labelBlocks.Clear();
        var count = Labels.Count;
        for (var i = 0; i < count; i++)
        {
            var center = KnobMath.LabelCenter(KnobMath.DetentAngle(i, count));
            var block = new TextBlock
            {
                Text = Labels[i],
                Width = 40,
                Height = 16,
                TextAlignment = TextAlignment.Center,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
            };
            block.SetResourceReference(TextBlock.FontFamilyProperty, "SR.Font.Silk");
            Canvas.SetLeft(block, center.X - 20);
            Canvas.SetTop(block, center.Y - 8);
            canvas.Children.Add(block);
            _labelBlocks.Add(block);
        }

        ColorLabels();
    }

    private void ColorLabels()
    {
        for (var i = 0; i < _labelBlocks.Count; i++)
        {
            _labelBlocks[i].Foreground = i == SelectedIndex ? SelectedLabelBrush : LabelBrush;
        }
    }

    private sealed class RotaryKnobAutomationPeer(RotaryKnob owner) : ButtonBaseAutomationPeer(owner), IInvokeProvider, IValueProvider
    {
        private RotaryKnob Knob => (RotaryKnob)Owner;

        public bool IsReadOnly => false;

        public string Value => Knob.SelectedLabel;

        public void Invoke()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            Knob.Step(+1, wrap: true);
        }

        public void SetValue(string value)
        {
            var index = Knob.Labels.IndexOf(value);
            if (index < 0)
            {
                throw new ArgumentException($"No detent is labeled '{value}'.", nameof(value));
            }

            Knob.SetCurrentValue(SelectedIndexProperty, index);
        }

        public override object GetPattern(PatternInterface patternInterface) => patternInterface switch
        {
            PatternInterface.Invoke or PatternInterface.Value => this,
            _ => base.GetPattern(patternInterface),
        };

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string GetClassNameCore() => nameof(RotaryKnob);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            if (string.IsNullOrEmpty(name))
            {
                name = Knob.Caption ?? string.Empty;
            }

            return string.Create(CultureInfo.InvariantCulture, $"{name} {Knob.SelectedLabel}").Trim();
        }
    }
}

/// <summary>Turns "20,22,24" in XAML into a list of strings.</summary>
public sealed class CommaListConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string s ? Parse(s) : base.ConvertFrom(context, culture, value);

    /// <summary>Splits on commas and trims each entry. Empty input gives an empty list.</summary>
    public static IList<string> Parse(string text) =>
        new ReadOnlyCollection<string>(text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
}
