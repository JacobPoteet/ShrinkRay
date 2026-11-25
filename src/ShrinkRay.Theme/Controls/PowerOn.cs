using System.Windows;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// The power-on self-test. Set <c>IsTesting</c> on a panel and everything inside it answers: every
/// segment and LED lights, every lamp comes on and the needles swing to full scale. Clearing it
/// puts each back on its own reading.
/// </summary>
public static class PowerOn
{
    public static readonly DependencyProperty IsTestingProperty = DependencyProperty.RegisterAttached(
        "IsTesting", typeof(bool), typeof(PowerOn),
        new FrameworkPropertyMetadata(false,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static bool GetIsTesting(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsTestingProperty);
    }

    public static void SetIsTesting(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsTestingProperty, value);
    }
}
