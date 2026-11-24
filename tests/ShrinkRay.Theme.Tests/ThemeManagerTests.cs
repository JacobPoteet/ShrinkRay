using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ShrinkRay.Theme.Tests;

/// <summary>ThemeManager is static, so these tests run one at a time.</summary>
[CollectionDefinition(nameof(ThemeManagerCollection), DisableParallelization = true)]
public class ThemeManagerCollection;

[Collection(nameof(ThemeManagerCollection))]
public sealed class ThemeManagerTests : IDisposable
{
    private readonly ResourceDictionary _target = new();

    public ThemeManagerTests()
    {
        ThemeManager.Attach(_target);
        ThemeManager.Apply(Phosphor.Amber, Chassis.Beige);
    }

    public void Dispose() => ThemeManager.Attach(null);

    [Fact]
    public void Apply_writes_every_runtime_key() =>
        Assert.All(ThemeKeys.All, key => Assert.True(_target.Contains(key), $"missing {key}"));

    [Theory]
    [InlineData(Phosphor.Amber, "#FFFFB000", "#FF3A2806")]
    [InlineData(Phosphor.Green, "#FF4DFF8F", "#FF0C3A1F")]
    [InlineData(Phosphor.Ice, "#FF86E4FF", "#FF0E3442")]
    public void Phosphor_switches_lit_and_unlit_colors(Phosphor phosphor, string on, string dim)
    {
        ThemeManager.SetPhosphor(phosphor);

        Assert.Equal(phosphor, ThemeManager.Phosphor);
        Assert.Equal(on, BrushColor(ThemeKeys.Phosphor));
        Assert.Equal(dim, BrushColor(ThemeKeys.PhosphorDim));
        Assert.Equal(ColorFrom(on), (Color)_target[ThemeKeys.PhosphorColor]);
        Assert.Equal(ColorFrom(on), ((DropShadowEffect)_target[ThemeKeys.PhosphorTextGlow]).Color);
    }

    [Theory]
    [InlineData(Chassis.Beige, "#FFD8CFBB", "#FFE9E2D1", "#FFB4A98F", "#FF3B352A")]
    [InlineData(Chassis.Graphite, "#FF4B4843", "#FF5E5A54", "#FF33302C", "#FFE4DDCC")]
    public void Chassis_switches_the_abs_tones_and_ink(Chassis chassis, string abs, string hi, string lo, string ink)
    {
        ThemeManager.SetChassis(chassis);

        Assert.Equal(chassis, ThemeManager.Chassis);
        Assert.Equal(abs, BrushColor(ThemeKeys.Abs));
        Assert.Equal(hi, BrushColor(ThemeKeys.AbsHi));
        Assert.Equal(lo, BrushColor(ThemeKeys.AbsLo));
        Assert.Equal(ink, BrushColor(ThemeKeys.Ink));

        var cap = (LinearGradientBrush)_target[ThemeKeys.KeyCap];
        Assert.Equal(ColorFrom(hi), cap.GradientStops[0].Color);
        Assert.Equal(ColorFrom(abs), cap.GradientStops[1].Color);
    }

    [Fact]
    public void Switching_one_axis_keeps_the_other()
    {
        ThemeManager.Apply(Phosphor.Ice, Chassis.Graphite);
        ThemeManager.SetPhosphor(Phosphor.Green);
        Assert.Equal(Chassis.Graphite, ThemeManager.Chassis);
        Assert.Equal("#FF4B4843", BrushColor(ThemeKeys.Abs));

        ThemeManager.SetChassis(Chassis.Beige);
        Assert.Equal(Phosphor.Green, ThemeManager.Phosphor);
        Assert.Equal("#FF4DFF8F", BrushColor(ThemeKeys.Phosphor));
    }

    [Fact]
    public void Apply_raises_ThemeChanged_after_writing()
    {
        string? seen = null;
        void Handler(object? s, EventArgs e) => seen = BrushColor(ThemeKeys.Phosphor);
        ThemeManager.ThemeChanged += Handler;
        try
        {
            ThemeManager.SetPhosphor(Phosphor.Ice);
        }
        finally
        {
            ThemeManager.ThemeChanged -= Handler;
        }

        Assert.Equal("#FF86E4FF", seen);
    }

    [Fact]
    public void Written_resources_are_frozen_so_any_thread_can_read_them() =>
        Assert.All(ThemeKeys.All.Select(k => _target[k]).OfType<Freezable>(), f => Assert.True(f.IsFrozen));

    [Fact]
    public void ApplyTo_leaves_the_manager_state_alone()
    {
        var other = new ResourceDictionary();
        ThemeManager.ApplyTo(other, Phosphor.Green, Chassis.Graphite);

        Assert.Equal(Phosphor.Amber, ThemeManager.Phosphor);
        Assert.Equal(Chassis.Beige, ThemeManager.Chassis);
        Assert.Equal("#FF4DFF8F", ((SolidColorBrush)other[ThemeKeys.Phosphor]).Color.ToString());
        Assert.Equal("#FFFFB000", BrushColor(ThemeKeys.Phosphor));
    }

    [Fact]
    public void Unknown_palettes_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PhosphorPalette.For((Phosphor)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChassisPalette.For((Chassis)42));
    }

    private string BrushColor(string key) => ((SolidColorBrush)_target[key]).Color.ToString();

    private static Color ColorFrom(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

public class PaletteContrastTests
{
    // Text printed on the chassis must stay readable on both ends of the card gradient.
    [Theory]
    [InlineData(Chassis.Beige)]
    [InlineData(Chassis.Graphite)]
    public void Printed_inks_clear_wcag_contrast_on_the_chassis(Chassis chassis)
    {
        var c = ChassisPalette.For(chassis);
        foreach (var surface in new[] { c.AbsHi, c.Abs })
        {
            Assert.True(Contrast(c.Ink, surface) >= 4.5, $"ink on {surface}");
            Assert.True(Contrast(c.InkSoft, surface) >= 4.5, $"caption ink on {surface}");
            Assert.True(Contrast(c.LinePrint, surface) >= 4.5, $"line printer ink on {surface}");
            Assert.True(Contrast(c.SelectInk, surface) >= 4.5, $"selected detent ink on {surface}");
            Assert.True(Contrast(c.MarkerInk, surface) >= 3, $"marker (large text) on {surface}");
        }
    }

    [Theory]
    [InlineData(Phosphor.Amber)]
    [InlineData(Phosphor.Green)]
    [InlineData(Phosphor.Ice)]
    public void Phosphor_reads_on_tube_black_and_ghost_segments_stay_faint(Phosphor phosphor)
    {
        var p = PhosphorPalette.For(phosphor);
        var crt = Color.FromRgb(0x0D, 0x0A, 0x05);
        Assert.True(Contrast(p.On, crt) >= 7);
        Assert.True(Contrast(p.Dim, crt) < 2, "unlit segments should only just show");
    }

    private static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c) =>
        0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(byte v)
    {
        var s = v / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
