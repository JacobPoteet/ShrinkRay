using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Tests;

public class RollMathTests
{
    [Theory]
    [InlineData(0.0, "  1.0")]
    [InlineData(0.5, "  2.0")]
    [InlineData(1.0, "  3.0")]
    public void A_climb_reads_between_the_two_in_the_new_width(double progress, string expected) =>
        Assert.Equal(expected, RollMath.Frame("  1.0", "  3.0", progress));

    [Fact]
    public void The_count_moves_in_clicks()
    {
        var click = 1.0 / RollMath.Steps;
        Assert.Equal(RollMath.Frame("0", "240", click), RollMath.Frame("0", "240", click * 1.9));
        Assert.Equal("10", RollMath.Frame("0", "240", click).Trim());
    }

    [Fact]
    public void Two_decimals_stay_two() => Assert.Equal(" 1.50", RollMath.Frame(" 1.00", " 2.00", 0.5));

    [Theory]
    [InlineData(null, "  3.0")]
    [InlineData("--", "  3.0")]
    [InlineData("  5.0", "  3.0")]
    [InlineData("999.9", " 1.00")]
    [InlineData("  1.0", "--")]
    public void Anything_but_a_climb_reads_the_new_value_at_once(string? from, string to) =>
        Assert.Equal(to, RollMath.Frame(from, to, 0));
}
