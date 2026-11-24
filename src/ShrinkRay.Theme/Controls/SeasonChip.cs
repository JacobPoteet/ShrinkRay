using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// A small keycap that picks one season. Chips that share a GroupName act as one selector,
/// so this is a radio button. The lamp shows the season's state: Active and blinking while it
/// encodes, Ok when every episode is handled.
/// </summary>
public class SeasonChip : RadioButton
{
    /// <summary>How far the cap drops when selected.</summary>
    public const double Throw = 3;

    public static readonly DependencyProperty LampProperty = DependencyProperty.Register(
        nameof(Lamp), typeof(LampState), typeof(SeasonChip), new PropertyMetadata(LampState.Off));

    public static readonly DependencyProperty LampBlinkProperty = DependencyProperty.Register(
        nameof(LampBlink), typeof(bool), typeof(SeasonChip), new PropertyMetadata(false));

    public static readonly DependencyProperty ShowLampProperty = DependencyProperty.Register(
        nameof(ShowLamp), typeof(bool), typeof(SeasonChip), new PropertyMetadata(true));

    static SeasonChip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SeasonChip), new FrameworkPropertyMetadata(typeof(SeasonChip)));
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

    public bool ShowLamp
    {
        get => (bool)GetValue(ShowLampProperty);
        set => SetValue(ShowLampProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SeasonChipAutomationPeer(this);

    private sealed class SeasonChipAutomationPeer(SeasonChip owner) : RadioButtonAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(SeasonChip);
    }
}
