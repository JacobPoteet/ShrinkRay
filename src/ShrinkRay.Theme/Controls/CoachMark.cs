using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShrinkRay.Theme.Controls;

/// <summary>The edge of a <see cref="CoachMark"/> its pointer sticks out of.</summary>
public enum CoachPointer
{
    Left,
    Bottom,
}

/// <summary>
/// A phosphor callout that points at one part of a panel: a small screen with a line of text, a
/// step count, and keys for Next and Skip. Place it beside its target and slide the pointer along
/// the edge with <see cref="PointerOffset"/>.
/// </summary>
public class CoachMark : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(CoachMark), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(string), typeof(CoachMark), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PointerProperty = DependencyProperty.Register(
        nameof(Pointer), typeof(CoachPointer), typeof(CoachMark), new PropertyMetadata(CoachPointer.Left));

    public static readonly DependencyProperty PointerOffsetProperty = DependencyProperty.Register(
        nameof(PointerOffset), typeof(double), typeof(CoachMark), new PropertyMetadata(20.0));

    public static readonly DependencyProperty NextLabelProperty = DependencyProperty.Register(
        nameof(NextLabel), typeof(string), typeof(CoachMark), new PropertyMetadata("Next"));

    public static readonly DependencyProperty NextCommandProperty = DependencyProperty.Register(
        nameof(NextCommand), typeof(ICommand), typeof(CoachMark), new PropertyMetadata(null));

    public static readonly DependencyProperty SkipCommandProperty = DependencyProperty.Register(
        nameof(SkipCommand), typeof(ICommand), typeof(CoachMark), new PropertyMetadata(null));

    static CoachMark()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(CoachMark), new FrameworkPropertyMetadata(typeof(CoachMark)));
        FocusableProperty.OverrideMetadata(typeof(CoachMark), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(CoachMark), new FrameworkPropertyMetadata(false));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Where this mark sits in its tour, such as "1 of 3".</summary>
    public string Step
    {
        get => (string)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public CoachPointer Pointer
    {
        get => (CoachPointer)GetValue(PointerProperty);
        set => SetValue(PointerProperty, value);
    }

    /// <summary>How far along its edge the pointer starts: down the left edge, or across the bottom.</summary>
    public double PointerOffset
    {
        get => (double)GetValue(PointerOffsetProperty);
        set => SetValue(PointerOffsetProperty, value);
    }

    public string NextLabel
    {
        get => (string)GetValue(NextLabelProperty);
        set => SetValue(NextLabelProperty, value);
    }

    public ICommand? NextCommand
    {
        get => (ICommand?)GetValue(NextCommandProperty);
        set => SetValue(NextCommandProperty, value);
    }

    /// <summary>Ends the tour. The Skip key is hidden without one.</summary>
    public ICommand? SkipCommand
    {
        get => (ICommand?)GetValue(SkipCommandProperty);
        set => SetValue(SkipCommandProperty, value);
    }

    /// <summary>Puts the keyboard on the Next key.</summary>
    public void FocusNext()
    {
        ApplyTemplate();
        (GetTemplateChild("PART_Next") as UIElement)?.Focus();
    }
}
