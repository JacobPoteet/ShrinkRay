using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ShrinkRay.Theme;

/// <summary>
/// Switches phosphor and chassis at runtime by rewriting the keys in <see cref="ThemeKeys"/>.
/// Controls pick the change up through DynamicResource.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? _target;

    /// <summary>The phosphor applied last.</summary>
    public static Phosphor Phosphor { get; private set; } = Phosphor.Amber;

    /// <summary>The chassis applied last.</summary>
    public static Chassis Chassis { get; private set; } = Chassis.Beige;

    /// <summary>Raised after <see cref="Apply"/> writes new resources.</summary>
    public static event EventHandler? ThemeChanged;

    /// <summary>
    /// Sets the dictionary <see cref="Apply"/> writes into. Without it, the manager writes into
    /// <c>Application.Current.Resources</c>.
    /// </summary>
    public static void Attach(ResourceDictionary? target) => _target = target;

    public static void SetPhosphor(Phosphor phosphor) => Apply(phosphor, Chassis);

    public static void SetChassis(Chassis chassis) => Apply(Phosphor, chassis);

    /// <summary>Writes the palette for <paramref name="phosphor"/> and <paramref name="chassis"/>.</summary>
    public static void Apply(Phosphor phosphor, Chassis chassis)
    {
        var target = _target ?? Application.Current?.Resources
            ?? throw new InvalidOperationException(
                "ThemeManager has no target. Call Attach or create a WPF Application first.");

        ApplyTo(target, phosphor, chassis);
        Phosphor = phosphor;
        Chassis = chassis;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Writes every runtime key into <paramref name="target"/> without touching the manager's state.</summary>
    public static void ApplyTo(ResourceDictionary target, Phosphor phosphor, Chassis chassis)
    {
        ArgumentNullException.ThrowIfNull(target);
        WritePhosphor(target, PhosphorPalette.For(phosphor));
        WriteChassis(target, ChassisPalette.For(chassis));
    }

    private static void WritePhosphor(ResourceDictionary d, PhosphorPalette p)
    {
        d[ThemeKeys.PhosphorColor] = p.On;
        d[ThemeKeys.PhosphorDimColor] = p.Dim;
        d[ThemeKeys.PhosphorGlowColor] = p.Glow;
        d[ThemeKeys.Phosphor] = Solid(p.On);
        d[ThemeKeys.PhosphorDim] = Solid(p.Dim);
        d[ThemeKeys.PhosphorGlow] = Solid(p.Glow);
        d[ThemeKeys.PhosphorRowTint] = Solid(Color.FromArgb(0x38, p.On.R, p.On.G, p.On.B));

        var halo = new RadialGradientBrush();
        halo.GradientStops.Add(new GradientStop(p.Glow, 0.0));
        halo.GradientStops.Add(new GradientStop(p.Glow, 0.42));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.On.R, p.On.G, p.On.B), 1.0));
        halo.Freeze();
        d[ThemeKeys.PhosphorHalo] = halo;

        // The light a lit readout throws onto the plate around it.
        var spill = new RadialGradientBrush();
        spill.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, p.On.R, p.On.G, p.On.B), 0.0));
        spill.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, p.On.R, p.On.G, p.On.B), 0.55));
        spill.GradientStops.Add(new GradientStop(Color.FromArgb(0, p.On.R, p.On.G, p.On.B), 1.0));
        spill.Freeze();
        d[ThemeKeys.PhosphorSpill] = spill;

        d[ThemeKeys.PhosphorTextGlow] = Glow(p, blur: 7, opacity: p.Glow.A / 255.0);
        d[ThemeKeys.PhosphorStrongGlow] = Glow(p, blur: 10, opacity: 1);
        d[ThemeKeys.PhosphorBloom] = Glow(p, blur: 32, opacity: 0.85);
    }

    private static void WriteChassis(ResourceDictionary d, ChassisPalette c)
    {
        d[ThemeKeys.AbsColor] = c.Abs;
        d[ThemeKeys.AbsHiColor] = c.AbsHi;
        d[ThemeKeys.AbsLoColor] = c.AbsLo;
        d[ThemeKeys.InkColor] = c.Ink;
        d[ThemeKeys.Abs] = Solid(c.Abs);
        d[ThemeKeys.AbsHi] = Solid(c.AbsHi);
        d[ThemeKeys.AbsLo] = Solid(c.AbsLo);
        d[ThemeKeys.Ink] = Solid(c.Ink);
        d[ThemeKeys.InkSoft] = Solid(c.InkSoft);
        d[ThemeKeys.LinePrinterInk] = Solid(c.LinePrint);
        d[ThemeKeys.MarkerInk] = Solid(c.MarkerInk);
        d[ThemeKeys.SelectInk] = Solid(c.SelectInk);

        // CSS: linear-gradient(175deg, hi 0%, abs 38%, lo 100%).
        var surface = new LinearGradientBrush
        {
            StartPoint = new Point(0.46, 0),
            EndPoint = new Point(0.54, 1),
        };
        surface.GradientStops.Add(new GradientStop(c.AbsHi, 0));
        surface.GradientStops.Add(new GradientStop(c.Abs, 0.38));
        surface.GradientStops.Add(new GradientStop(c.AbsLo, 1));
        surface.Freeze();
        d[ThemeKeys.ChassisSurface] = surface;

        d[ThemeKeys.KeyCap] = Vertical(c.AbsHi, c.Abs);
        d[ThemeKeys.BezelFill] = Vertical(c.Abs, c.AbsLo);
    }

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Vertical(Color top, Color bottom)
    {
        var brush = new LinearGradientBrush(top, bottom, 90);
        brush.Freeze();
        return brush;
    }

    private static DropShadowEffect Glow(PhosphorPalette p, double blur, double opacity)
    {
        var effect = new DropShadowEffect
        {
            Color = p.On,
            ShadowDepth = 0,
            BlurRadius = blur,
            Opacity = opacity,
            RenderingBias = RenderingBias.Quality,
        };
        effect.Freeze();
        return effect;
    }
}
