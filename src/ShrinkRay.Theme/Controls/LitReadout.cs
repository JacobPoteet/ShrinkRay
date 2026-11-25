using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// A drawn readout whose lit parts sit on their own layers above the unlit ones, so the glow and
/// the wide bloom come from the lit parts alone.
/// </summary>
public abstract class LitReadout : FrameworkElement
{
    public static readonly DependencyProperty GlowProperty = DependencyProperty.Register(
        nameof(Glow), typeof(bool), typeof(LitReadout), new PropertyMetadata(false));

    public static readonly DependencyProperty GlowEffectProperty = DependencyProperty.Register(
        nameof(GlowEffect), typeof(Effect), typeof(LitReadout),
        new PropertyMetadata(null, (d, e) => ((LitReadout)d)._lit.Glow = (Effect?)e.NewValue));

    public static readonly DependencyProperty BloomEffectProperty = DependencyProperty.Register(
        nameof(BloomEffect), typeof(Effect), typeof(LitReadout),
        new PropertyMetadata(null, (d, e) => ((LitReadout)d)._bloom.Glow = (Effect?)e.NewValue));

    // The lit layer sits inside the bloom layer, so the wide halo is cast by the tight glow as well.
    private readonly LitLayer _lit = new();
    private readonly BloomLayer _bloom = new();

    protected LitReadout()
    {
        _bloom.Children.Add(_lit);
        AddVisualChild(_bloom);
    }

    /// <summary>Turns on the phosphor glow. The theme answers with the two effects.</summary>
    public bool Glow
    {
        get => (bool)GetValue(GlowProperty);
        set => SetValue(GlowProperty, value);
    }

    /// <summary>The tight glow around the lit parts. The theme sets it when <see cref="Glow"/> is on.</summary>
    public Effect? GlowEffect
    {
        get => (Effect?)GetValue(GlowEffectProperty);
        set => SetValue(GlowEffectProperty, value);
    }

    /// <summary>The wide halo outside that glow, also set by the theme.</summary>
    public Effect? BloomEffect
    {
        get => (Effect?)GetValue(BloomEffectProperty);
        set => SetValue(BloomEffectProperty, value);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _bloom : throw new System.ArgumentOutOfRangeException(nameof(index));

    protected sealed override void OnRender(DrawingContext dc)
    {
        using var lit = _lit.RenderOpen();
        Draw(dc, lit);
    }

    /// <summary>Draws the readout: dark parts into <paramref name="unlit"/>, glowing ones into <paramref name="lit"/>.</summary>
    protected abstract void Draw(DrawingContext unlit, DrawingContext lit);

    // Visual keeps its effect setter protected, hence the two subclasses.
    private sealed class LitLayer : DrawingVisual
    {
        public Effect? Glow
        {
            set => VisualEffect = value;
        }
    }

    private sealed class BloomLayer : ContainerVisual
    {
        public Effect? Glow
        {
            set => VisualEffect = value;
        }
    }
}
