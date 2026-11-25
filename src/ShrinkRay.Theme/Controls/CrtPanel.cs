using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// A CRT screen: tube-black glass, phosphor text in scientifica at 22 px with a glow, tiled
/// scanlines, a vignette and a faint flicker. The flicker holds still when motion is reduced.
/// </summary>
public class CrtPanel : ContentControl
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(CrtPanel), new PropertyMetadata(18.0));

    public static readonly DependencyProperty IsFlickerEnabledProperty = DependencyProperty.Register(
        nameof(IsFlickerEnabled), typeof(bool), typeof(CrtPanel), new PropertyMetadata(true, OnFlickerChanged));

    public static readonly DependencyProperty IsGlowEnabledProperty = DependencyProperty.Register(
        nameof(IsGlowEnabled), typeof(bool), typeof(CrtPanel), new PropertyMetadata(true));

    /// <summary>The CSS blur of the inner tube shadow.</summary>
    public static readonly DependencyProperty TubeShadowProperty = DependencyProperty.Register(
        nameof(TubeShadow), typeof(double), typeof(CrtPanel), new PropertyMetadata(40.0));

    static CrtPanel()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(CrtPanel), new FrameworkPropertyMetadata(typeof(CrtPanel)));
        FocusableProperty.OverrideMetadata(typeof(CrtPanel), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(CrtPanel), new FrameworkPropertyMetadata(false));
    }

    public CrtPanel()
    {
        Loaded += (_, _) =>
        {
            MotionPolicy.Changed += OnMotionChanged;
            UpdateFlicker();
        };
        Unloaded += (_, _) => MotionPolicy.Changed -= OnMotionChanged;
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public bool IsFlickerEnabled
    {
        get => (bool)GetValue(IsFlickerEnabledProperty);
        set => SetValue(IsFlickerEnabledProperty, value);
    }

    public bool IsGlowEnabled
    {
        get => (bool)GetValue(IsGlowEnabledProperty);
        set => SetValue(IsGlowEnabledProperty, value);
    }

    public double TubeShadow
    {
        get => (double)GetValue(TubeShadowProperty);
        set => SetValue(TubeShadowProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateFlicker();
        ApplyWarmUp();
    }

    // Draws the current frame of CrtWarmUp: the line, and the picture opening around it.
    internal void ApplyWarmUp()
    {
        if (GetTemplateChild("PART_Screen") is not FrameworkElement screen
            || GetTemplateChild("PART_Line") is not FrameworkElement line)
        {
            return;
        }

        var progress = CrtWarmUp.GetProgress(this);
        if (progress >= 1)
        {
            screen.ClearValue(RenderTransformProperty);
            line.Visibility = Visibility.Collapsed;
            return;
        }

        screen.RenderTransformOrigin = new Point(0.5, 0.5);
        screen.RenderTransform = new ScaleTransform(1, WarmUpMath.Opening(progress));
        line.RenderTransformOrigin = new Point(0.5, 0.5);
        line.RenderTransform = new ScaleTransform(WarmUpMath.LineWidth(progress), 1);
        line.Opacity = WarmUpMath.LineOpacity(progress);
        line.Visibility = Visibility.Visible;
    }

    private static void OnFlickerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CrtPanel)d).UpdateFlicker();

    private void OnMotionChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateFlicker);

    private void UpdateFlicker()
    {
        if (GetTemplateChild("PART_Overlay") is not UIElement overlay)
        {
            return;
        }

        if (!IsFlickerEnabled || !MotionPolicy.IsMotionEnabled)
        {
            overlay.BeginAnimation(OpacityProperty, null);
            return;
        }

        // CSS: the scanline and vignette layer runs flicker 5s; 0%,100% 1; 48% .94; 50% 1; 73% .97.
        var frames = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(5),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.94, KeyTime.FromPercent(0.48)));
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.50)));
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(0.97, KeyTime.FromPercent(0.73)));
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        overlay.BeginAnimation(OpacityProperty, frames);
    }
}
