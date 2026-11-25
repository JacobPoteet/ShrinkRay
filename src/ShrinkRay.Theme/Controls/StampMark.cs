using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// A rubber-stamp mark: the content in an inked box, set a few degrees off square. While
/// <see cref="IsPressing"/> is on, a new mark comes down onto the paper once. Held motion shows it
/// already printed.
/// </summary>
[TemplatePart(Name = "PART_Mark", Type = typeof(FrameworkElement))]
public class StampMark : ContentControl
{
    public static readonly DependencyProperty IsPressingProperty = DependencyProperty.Register(
        nameof(IsPressing), typeof(bool), typeof(StampMark),
        new PropertyMetadata(false, (d, e) => ((StampMark)d).Press()));

    private static readonly TimeSpan Fall = TimeSpan.FromMilliseconds(180);

    private ScaleTransform? _scale;
    private FrameworkElement? _mark;

    static StampMark()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StampMark), new FrameworkPropertyMetadata(typeof(StampMark)));
        FocusableProperty.OverrideMetadata(typeof(StampMark), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(StampMark), new FrameworkPropertyMetadata(false));
    }

    public bool IsPressing
    {
        get => (bool)GetValue(IsPressingProperty);
        set => SetValue(IsPressingProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _mark = GetTemplateChild("PART_Mark") as FrameworkElement;
        _scale = null;
        if (_mark is not null)
        {
            _scale = new ScaleTransform(1, 1);
            var transforms = new TransformGroup();
            transforms.Children.Add(_scale);
            transforms.Children.Add(new RotateTransform(-5));
            _mark.RenderTransform = transforms;
            _mark.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        Press();
    }

    private void Press()
    {
        if (_scale is null || _mark is null || !IsPressing || !MotionPolicy.IsMotionEnabled)
        {
            return;
        }

        // It drops fast and stops dead, as a stamp does when it meets the desk.
        var drop = new DoubleAnimation(2.4, 1, Fall) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, drop);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, drop);
        _mark.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Fall));
    }
}
