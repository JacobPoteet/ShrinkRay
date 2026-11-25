using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ShrinkRay.Theme.Controls;

/// <summary>Lamp states shared by <see cref="Lamp"/>, key lamps and season chips.</summary>
public enum LampState
{
    Off,
    Active,
    Ok,
    Red,
}

/// <summary>
/// A chunky transport key that drops 6 px when pressed. A real <see cref="Button"/>.
/// Set <see cref="IsRecord"/> for the signal-red Encode key. Use <see cref="PianoLatchKey"/> for
/// a key that stays down.
/// </summary>
public class PianoKey : Button
{
    /// <summary>How far the cap drops when pressed or latched.</summary>
    public const double Throw = 6;

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(PianoKey), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(Geometry), typeof(PianoKey), new PropertyMetadata(null));

    public static readonly DependencyProperty IsRecordProperty = DependencyProperty.Register(
        nameof(IsRecord), typeof(bool), typeof(PianoKey), new PropertyMetadata(false));

    public static readonly DependencyProperty ShowLampProperty = DependencyProperty.Register(
        nameof(ShowLamp), typeof(bool), typeof(PianoKey), new PropertyMetadata(false));

    public static readonly DependencyProperty LampProperty = DependencyProperty.Register(
        nameof(Lamp), typeof(LampState), typeof(PianoKey), new PropertyMetadata(LampState.Off));

    public static readonly DependencyProperty LampBlinkProperty = DependencyProperty.Register(
        nameof(LampBlink), typeof(bool), typeof(PianoKey), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey IsKeyDownPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsKeyDown), typeof(bool), typeof(PianoKey), new PropertyMetadata(false, OnIsKeyDownChanged));

    /// <summary>True while the cap is down: pressed, or latched for <see cref="PianoLatchKey"/>.</summary>
    public static readonly DependencyProperty IsKeyDownProperty = IsKeyDownPropertyKey.DependencyProperty;

    static PianoKey()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PianoKey), new FrameworkPropertyMetadata(typeof(PianoKey)));
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsRecord
    {
        get => (bool)GetValue(IsRecordProperty);
        set => SetValue(IsRecordProperty, value);
    }

    public bool ShowLamp
    {
        get => (bool)GetValue(ShowLampProperty);
        set => SetValue(ShowLampProperty, value);
    }

    public LampState Lamp
    {
        get => (LampState)GetValue(LampProperty);
        set => SetValue(LampProperty, value);
    }

    public bool LampBlink
    {
        get => (bool)GetValue(LampBlinkProperty);
        set => SetValue(LampBlinkProperty, value);
    }

    public bool IsKeyDown => (bool)GetValue(IsKeyDownProperty);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        KeyThrow.Snap(this, IsKeyDown);
    }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        SetValue(IsKeyDownPropertyKey, IsPressed);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PianoKeyAutomationPeer(this);

    private static void OnIsKeyDownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyThrow.Animate((Control)d, (bool)e.NewValue);

    private sealed class PianoKeyAutomationPeer(PianoKey owner) : ButtonAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(PianoKey);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrEmpty(name) ? ((PianoKey)Owner).Label ?? string.Empty : name;
        }
    }
}

/// <summary>
/// A piano key that latches down until pressed again, like Pause on a deck.
/// A real <see cref="ToggleButton"/>.
/// </summary>
public class PianoLatchKey : ToggleButton
{
    public static readonly DependencyProperty LabelProperty = PianoKey.LabelProperty.AddOwner(typeof(PianoLatchKey));
    public static readonly DependencyProperty IconProperty = PianoKey.IconProperty.AddOwner(typeof(PianoLatchKey));
    public static readonly DependencyProperty IsRecordProperty = PianoKey.IsRecordProperty.AddOwner(typeof(PianoLatchKey));
    public static readonly DependencyProperty ShowLampProperty = PianoKey.ShowLampProperty.AddOwner(typeof(PianoLatchKey));
    public static readonly DependencyProperty LampProperty = PianoKey.LampProperty.AddOwner(typeof(PianoLatchKey));
    public static readonly DependencyProperty LampBlinkProperty = PianoKey.LampBlinkProperty.AddOwner(typeof(PianoLatchKey));

    private static readonly DependencyPropertyKey IsKeyDownPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsKeyDown), typeof(bool), typeof(PianoLatchKey), new PropertyMetadata(false, OnIsKeyDownChanged));

    public static readonly DependencyProperty IsKeyDownProperty = IsKeyDownPropertyKey.DependencyProperty;

    static PianoLatchKey()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PianoLatchKey), new FrameworkPropertyMetadata(typeof(PianoLatchKey)));
        IsCheckedProperty.OverrideMetadata(typeof(PianoLatchKey),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsCheckedChanged));
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public Geometry? Icon
    {
        get => (Geometry?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsRecord
    {
        get => (bool)GetValue(IsRecordProperty);
        set => SetValue(IsRecordProperty, value);
    }

    public bool ShowLamp
    {
        get => (bool)GetValue(ShowLampProperty);
        set => SetValue(ShowLampProperty, value);
    }

    public LampState Lamp
    {
        get => (LampState)GetValue(LampProperty);
        set => SetValue(LampProperty, value);
    }

    public bool LampBlink
    {
        get => (bool)GetValue(LampBlinkProperty);
        set => SetValue(LampBlinkProperty, value);
    }

    public bool IsKeyDown => (bool)GetValue(IsKeyDownProperty);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        KeyThrow.Snap(this, IsKeyDown);
    }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        UpdateKeyDown();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PianoLatchKeyAutomationPeer(this);

    private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PianoLatchKey)d).UpdateKeyDown();

    private static void OnIsKeyDownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyThrow.Animate((Control)d, (bool)e.NewValue);

    /// <summary>Skips the local flip when UI Automation clicks the key, so the binding keeps the latch.</summary>
    protected override void OnToggle()
    {
        if (!_automationClick)
        {
            base.OnToggle();
        }
    }

    private void UpdateKeyDown() => SetValue(IsKeyDownPropertyKey, IsPressed || IsChecked == true);

    private bool _automationClick;

    /// <summary>Acts like a click without flipping <see cref="ToggleButton.IsChecked"/>: raises Click and runs the command.</summary>
    private void ClickFromAutomation()
    {
        _automationClick = true;
        try
        {
            OnClick();
        }
        finally
        {
            _automationClick = false;
        }
    }

    private sealed class PianoLatchKeyAutomationPeer(PianoLatchKey owner) : ToggleButtonAutomationPeer(owner), IToggleProvider
    {
        /// <summary>UI Automation Toggle is a click. The command decides what the latch shows.</summary>
        void IToggleProvider.Toggle()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            ((PianoLatchKey)Owner).ClickFromAutomation();
        }

        protected override string GetClassNameCore() => nameof(PianoLatchKey);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrEmpty(name) ? ((PianoLatchKey)Owner).Label ?? string.Empty : name;
        }
    }
}

/// <summary>Moves a key cap (template part PART_Cap) down and back up.</summary>
internal static class KeyThrow
{
    private static readonly Duration Travel = new(TimeSpan.FromMilliseconds(60));

    public static void Animate(Control key, bool down)
    {
        if (Transform(key) is not { } t)
        {
            return;
        }

        var to = down ? PianoKey.Throw : 0;
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(to, Travel));
        if (key.Template?.FindName("PART_Soft", key) is UIElement soft)
        {
            soft.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(down ? 0.35 : 1, Travel));
        }
    }

    public static void Snap(Control key, bool down)
    {
        if (Transform(key) is not { } t)
        {
            return;
        }

        t.BeginAnimation(TranslateTransform.YProperty, null);
        t.Y = down ? PianoKey.Throw : 0;
        if (key.Template?.FindName("PART_Soft", key) is UIElement soft)
        {
            soft.BeginAnimation(UIElement.OpacityProperty, null);
            soft.Opacity = down ? 0.35 : 1;
        }
    }

    private static TranslateTransform? Transform(Control key)
    {
        if (key.Template?.FindName("PART_Cap", key) is not UIElement cap)
        {
            return null;
        }

        if (cap.RenderTransform is TranslateTransform { IsFrozen: false } t)
        {
            return t;
        }

        // Template freezables can be frozen; give each cap its own transform to animate.
        t = new TranslateTransform();
        cap.RenderTransform = t;
        return t;
    }
}
