using ShrinkRay.Theme.Controls;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Tests;

public class CubicBezierEaseTests
{
    [Fact]
    public void Ends_are_fixed()
    {
        Assert.Equal(0, CubicBezierEase.Needle.Ease(0));
        Assert.Equal(1, CubicBezierEase.Needle.Ease(1));
        Assert.Equal(0, CubicBezierEase.Needle.Ease(-0.5));
        Assert.Equal(1, CubicBezierEase.Needle.Ease(1.5));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void A_straight_curve_is_linear(double t) =>
        Assert.Equal(t, new CubicBezierEase(0, 0, 1, 1).Ease(t), 5);

    [Fact]
    public void Matches_css_ease_at_the_midpoint() =>
        // CSS "ease" is cubic-bezier(.25, .1, .25, 1); its value at x = .5 is about .8024.
        Assert.Equal(0.8024, new CubicBezierEase(0.25, 0.1, 0.25, 1).Ease(0.5), 3);

    [Fact]
    public void Needle_and_knob_overshoot_then_settle()
    {
        var samples = Enumerable.Range(1, 99).Select(i => i / 100.0).ToArray();
        Assert.True(samples.Max(CubicBezierEase.Needle.Ease) > 1.05);
        Assert.True(samples.Max(CubicBezierEase.Knob.Ease) > 1.03);
        Assert.Equal(1, CubicBezierEase.Needle.Ease(0.999), 2);
    }

    [Theory]
    [InlineData(-0.1, 0.5)]
    [InlineData(1.1, 0.5)]
    [InlineData(0.5, 1.2)]
    public void Control_x_must_lie_in_the_unit_range(double x1, double x2) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CubicBezierEase(x1, 0, x2, 1));
}

public class PixelFontMathTests
{
    [Theory]
    [InlineData(22, 1.0, 22)]
    [InlineData(22, 1.5, 22)]
    [InlineData(22, 2.0, 22)]
    [InlineData(22, 1.25, 26.4)]
    [InlineData(22, 1.75, 25.142857)]
    [InlineData(5, 1.0, 11)]
    [InlineData(11, 1.0, 11)]
    public void Snaps_to_whole_multiples_of_the_11_pixel_cell(double desired, double scale, double expected)
    {
        var snapped = PixelFontMath.Snap(desired, scale);
        Assert.Equal(expected, snapped, 5);

        var devicePixels = snapped * scale;
        Assert.Equal(0, devicePixels % PixelFontMath.Cell, 6);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(22, 0)]
    [InlineData(-4, 1)]
    [InlineData(double.NaN, 1)]
    public void Rejects_sizes_and_scales_that_are_not_positive(double desired, double scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelFontMath.Snap(desired, scale));
}

public class CommaListConverterTests
{
    [Fact]
    public void Splits_and_trims() =>
        Assert.Equal(["20", "22", "24"], CommaListConverter.Parse(" 20, 22 ,24 "));

    [Fact]
    public void Empty_text_gives_no_labels() => Assert.Empty(CommaListConverter.Parse(" , "));

    [Fact]
    public void Converts_from_xaml_strings()
    {
        var converter = new CommaListConverter();
        Assert.True(converter.CanConvertFrom(typeof(string)));
        var list = Assert.IsAssignableFrom<IList<string>>(converter.ConvertFrom("AMBER,GREEN,ICE"));
        Assert.Equal(3, list.Count);
    }
}
