using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ShrinkRay.Theme.Controls;

/// <summary>Where a lamp's word sits.</summary>
public enum LampLabelPlacement
{
    Right,
    Below,
}

/// <summary>
/// A panel lamp. Pair it with a word (the content), so a status never relies on color alone.
/// Active glows in the phosphor color, Ok in lamp green, Red in red. Blink pulses once a second
/// unless motion is reduced.
/// </summary>
public class Lamp : ContentControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(LampState), typeof(Lamp), new PropertyMetadata(LampState.Off, OnStateChanged));

    public static readonly DependencyProperty BlinkProperty = DependencyProperty.Register(
        nameof(Blink), typeof(bool), typeof(Lamp), new PropertyMetadata(false, OnBlinkChanged));

    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter), typeof(double), typeof(Lamp), new PropertyMetadata(9.0));

    public static readonly DependencyProperty LabelPlacementProperty = DependencyProperty.Register(
        nameof(LabelPlacement), typeof(LampLabelPlacement), typeof(Lamp), new PropertyMetadata(LampLabelPlacement.Right));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(Lamp), new PropertyMetadata(6.0));

    static Lamp()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Lamp), new FrameworkPropertyMetadata(typeof(Lamp)));
        FocusableProperty.OverrideMetadata(typeof(Lamp), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(Lamp), new FrameworkPropertyMetadata(false));
    }

    public Lamp()
    {
        Loaded += (_, _) =>
        {
            MotionPolicy.Changed += OnMotionChanged;
            UpdateBlink();
        };
        Unloaded += (_, _) => MotionPolicy.Changed -= OnMotionChanged;
    }

    public LampState State
    {
        get => (LampState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public bool Blink
    {
        get => (bool)GetValue(BlinkProperty);
        set => SetValue(BlinkProperty, value);
    }

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public LampLabelPlacement LabelPlacement
    {
        get => (LampLabelPlacement)GetValue(LabelPlacementProperty);
        set => SetValue(LabelPlacementProperty, value);
    }

    /// <summary>Gap between the lamp and its word.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateBlink();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LampAutomationPeer(this);

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var lamp = (Lamp)d;
        lamp.UpdateBlink();
        if (UIElementAutomationPeer.FromElement(lamp) is { } peer)
        {
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty,
                e.OldValue.ToString(), e.NewValue.ToString());
        }
    }

    private static void OnBlinkChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Lamp)d).UpdateBlink();

    private void OnMotionChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateBlink);

    private void UpdateBlink() => Blinker.Apply(GetTemplateChild("PART_Bulb") as UIElement,
        Blink && State != LampState.Off && MotionPolicy.IsMotionEnabled);

    private sealed class LampAutomationPeer(Lamp owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetClassNameCore() => nameof(Lamp);

        protected override string GetItemStatusCore() => ((Lamp)Owner).State.ToString();

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            var lamp = (Lamp)Owner;
            return lamp.Content is string s ? s : lamp.State.ToString();
        }
    }
}

/// <summary>The one-second on/off blink lamps use.</summary>
internal static class Blinker
{
    public static void Apply(UIElement? element, bool blink)
    {
        if (element is null)
        {
            return;
        }

        if (!blink)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            return;
        }

        // CSS: animation: blink 1s steps(2, start); 50% { opacity: .15 }.
        var frames = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(1),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500))));
        element.BeginAnimation(UIElement.OpacityProperty, frames);
    }
}
