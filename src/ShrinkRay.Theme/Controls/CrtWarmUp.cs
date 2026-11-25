using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// The CRT warm-up. <see cref="Play"/> warms up every phosphor screen under an element: each
/// <see cref="CrtPanel"/> and <see cref="SevenSegmentDisplay"/> opens from a bright line to full.
/// Meters, LED bars and printed parts stay as they are. It only draws, so nothing waits for it.
/// </summary>
public static class CrtWarmUp
{
    /// <summary>Where a screen stands in its warm-up, 0 to 1. At 1, the default, it is at rest.</summary>
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.RegisterAttached(
        "Progress", typeof(double), typeof(CrtWarmUp),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, OnProgressChanged));

    public static double GetProgress(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(ProgressProperty);
    }

    /// <summary>Holds a screen at one frame of the warm-up, as a still capture does.</summary>
    public static void SetProgress(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ProgressProperty, value);
    }

    /// <summary>Warms up every screen at or under <paramref name="root"/>. Does nothing while motion is held.</summary>
    public static void Play(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!MotionPolicy.IsMotionEnabled)
        {
            return;
        }

        foreach (var screen in Screens(root))
        {
            screen.BeginAnimation(ProgressProperty,
                new DoubleAnimation(0, 1, WarmUpMath.Duration) { FillBehavior = FillBehavior.Stop });
        }
    }

    /// <summary>The screens at or under <paramref name="root"/> in the visual tree.</summary>
    public static IEnumerable<FrameworkElement> Screens(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root is CrtPanel or SevenSegmentDisplay)
        {
            // A screen warms up as one piece, whatever it shows.
            yield return (FrameworkElement)root;
            yield break;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (var screen in Screens(VisualTreeHelper.GetChild(root, i)))
            {
                yield return screen;
            }
        }
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        (d as CrtPanel)?.ApplyWarmUp();
}
