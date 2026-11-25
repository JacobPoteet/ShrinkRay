using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Tests;

public class MarqueeMathTests
{
    [Theory]
    [InlineData(100, 200, false)]
    [InlineData(200, 200, false)]
    [InlineData(200.3, 200, false)]
    [InlineData(201, 200, true)]
    [InlineData(500, 200, true)]
    [InlineData(500, 0, false)]
    public void Overflow_needs_text_wider_than_the_room(double text, double room, bool overflows) =>
        Assert.Equal(overflows, MarqueeMath.Overflows(text, room));

    [Fact]
    public void Text_that_fits_has_no_distance_and_no_cycle()
    {
        Assert.Equal(0, MarqueeMath.Distance(150, 200));
        Assert.Equal(0, MarqueeMath.ScrollSeconds(0));
        Assert.Equal(0, MarqueeMath.CycleSeconds(0));
        Assert.Equal((0.0, 1.0), MarqueeMath.FrameAt(37, 0));
    }

    [Fact]
    public void The_distance_brings_the_end_into_view_with_a_little_to_spare()
    {
        Assert.Equal(300 + MarqueeMath.EndPad, MarqueeMath.Distance(500, 200));
    }

    [Fact]
    public void Scrolls_slowly_and_steadily()
    {
        Assert.InRange(MarqueeMath.Speed, 25, 30);
        var distance = MarqueeMath.Distance(500, 200);

        Assert.Equal(distance / MarqueeMath.Speed, MarqueeMath.ScrollSeconds(distance), 9);
        Assert.Equal(
            MarqueeMath.HoldSeconds + MarqueeMath.ScrollSeconds(distance) + MarqueeMath.HoldSeconds + (2 * MarqueeMath.FadeSeconds),
            MarqueeMath.CycleSeconds(distance),
            9);
    }

    [Fact]
    public void The_cycle_waits_scrolls_waits_then_fades_back()
    {
        const double distance = 140;
        var scroll = MarqueeMath.ScrollSeconds(distance);

        // Waits about two seconds at the start.
        Assert.Equal((0.0, 1.0), MarqueeMath.FrameAt(0, distance));
        Assert.Equal((0.0, 1.0), MarqueeMath.FrameAt(1.99, distance));

        // Then moves at the steady speed.
        var (offset, opacity) = MarqueeMath.FrameAt(MarqueeMath.HoldSeconds + 1, distance);
        Assert.Equal(MarqueeMath.Speed, offset, 9);
        Assert.Equal(1, opacity);

        // Waits at the end with the last letters showing.
        var end = MarqueeMath.HoldSeconds + scroll;
        Assert.Equal(distance, MarqueeMath.FrameAt(end + 0.5, distance).Offset, 9);
        Assert.Equal(1, MarqueeMath.FrameAt(end + 0.5, distance).Opacity);

        // Fades out while still, then in again at the start.
        var fadeStart = end + MarqueeMath.HoldSeconds;
        var halfOut = MarqueeMath.FrameAt(fadeStart + (MarqueeMath.FadeSeconds / 2), distance);
        Assert.Equal(distance, halfOut.Offset, 9);
        Assert.Equal(0.5, halfOut.Opacity, 9);
        var halfIn = MarqueeMath.FrameAt(fadeStart + MarqueeMath.FadeSeconds + (MarqueeMath.FadeSeconds / 2), distance);
        Assert.Equal(0, halfIn.Offset);
        Assert.Equal(0.5, halfIn.Opacity, 9);
    }

    [Fact]
    public void The_cycle_repeats_and_never_moves_backwards_mid_scroll()
    {
        const double distance = 90;
        var cycle = MarqueeMath.CycleSeconds(distance);

        Assert.Equal(MarqueeMath.FrameAt(1.3, distance), MarqueeMath.FrameAt(1.3 + (3 * cycle), distance));

        var last = 0.0;
        for (var t = 0.0; t < MarqueeMath.HoldSeconds + MarqueeMath.ScrollSeconds(distance); t += 0.05)
        {
            var offset = MarqueeMath.FrameAt(t, distance).Offset;
            Assert.True(offset >= last);
            last = offset;
        }
    }

    [Theory]
    [InlineData(240, 0.05)]
    [InlineData(24, 0.5)]
    [InlineData(10, 0.5)]
    [InlineData(0, 0.5)]
    public void The_soft_edge_is_twelve_pixels_and_never_past_the_middle(double viewport, double stop) =>
        Assert.Equal(stop, MarqueeMath.EdgeStop(viewport), 3);

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.4, false)]
    [InlineData(1, true)]
    [InlineData(80, true)]
    public void The_left_edge_fades_only_once_the_text_has_scrolled(double offset, bool fades) =>
        Assert.Equal(fades, MarqueeMath.FadesLeft(offset));
}
