using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Tests;

public class ReelMathTests
{
    [Theory]
    [InlineData(0, 34)]
    [InlineData(1, 80)]
    [InlineData(0.5, 57)]
    [InlineData(-1, 34)]
    [InlineData(2, 80)]
    [InlineData(double.NaN, 34)]
    public void Pack_radius_runs_from_the_hub_to_the_flange(double fill, double radius) =>
        Assert.Equal(radius, ReelMath.PackRadius(fill), 6);

    [Fact]
    public void Pack_stays_inside_the_flange_and_outside_the_hub()
    {
        Assert.True(ReelMath.PackRadius(0) > ReelMath.HubRadius);
        Assert.True(ReelMath.PackRadius(1) < ReelMath.FlangeRadius);
    }

    [Fact]
    public void Source_unwinds_while_output_fills_to_the_expected_ratio()
    {
        Assert.Equal(1, ReelMath.SourceFill(0));
        Assert.Equal(0, ReelMath.SourceFill(1));
        Assert.Equal(0, ReelMath.OutputFill(0, 0.45));
        Assert.Equal(0.225, ReelMath.OutputFill(0.5, 0.45), 6);
        Assert.Equal(0.45, ReelMath.OutputFill(1, 0.45), 6);
    }

    [Fact]
    public void Finished_output_reel_is_smaller_than_the_full_source_reel() =>
        Assert.True(ReelMath.PackRadius(ReelMath.OutputFill(1, 0.45)) < ReelMath.PackRadius(ReelMath.SourceFill(0)));

    [Theory]
    [InlineData(168, 1)]
    [InlineData(84, 0.5)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    public void Scale_maps_the_flange_to_the_element(double diameter, double scale) =>
        Assert.Equal(scale, ReelMath.Scale(diameter), 6);

    [Theory]
    [InlineData(200, 200, 3.2)]
    [InlineData(400, 200, 1.6)]
    [InlineData(1000, 200, 1.6)]
    [InlineData(100, 200, 6.4)]
    [InlineData(10, 200, 6.4)]
    [InlineData(300, 200, 3.2 / 1.5)]
    public void Reel_speed_follows_the_frame_rate_between_half_and_double(double fps, double usual, double seconds) =>
        Assert.Equal(seconds, ReelMath.SecondsPerTurn(3.2, fps, usual), 6);

    [Theory]
    [InlineData(205)]
    [InlineData(194)]
    public void Small_wobbles_in_the_frame_rate_keep_the_usual_speed(double fps) =>
        Assert.Equal(3.2, ReelMath.SecondsPerTurn(3.2, fps, 200), 6);

    [Theory]
    [InlineData(null, 200)]
    [InlineData(0.0, 200)]
    [InlineData(double.NaN, 200)]
    [InlineData(150.0, 0)]
    public void No_reading_keeps_the_usual_speed(double? fps, double usual) =>
        Assert.Equal(2.1, ReelMath.SecondsPerTurn(2.1, fps, usual), 6);
}

public class VuMathTests
{
    [Theory]
    [InlineData(0, -48)]
    [InlineData(8, 48)]
    [InlineData(4, 0)]
    [InlineData(-3, -48)]
    [InlineData(20, 48)]
    public void Needle_sweeps_48_degrees_either_side(double value, double angle) =>
        Assert.Equal(angle, VuMath.NeedleAngle(value, 0, 8), 6);

    [Fact]
    public void Needle_matches_the_kit_readings()
    {
        // The kit draws MBPS at 2.75 of 8 and LOAD at 78 of 100.
        Assert.Equal(-15, VuMath.NeedleAngle(2.75, 0, 8), 6);
        Assert.Equal(26.88, VuMath.NeedleAngle(78, 0, 100), 6);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(5, 1)]
    public void An_empty_or_reversed_range_rests_at_the_left_stop(double min, double max) =>
        Assert.Equal(-48, VuMath.NeedleAngle(3, min, max));

    [Fact]
    public void Not_a_number_rests_at_the_left_stop() =>
        Assert.Equal(-48, VuMath.NeedleAngle(double.NaN, 0, 8));

    [Fact]
    public void Ticks_spread_evenly_across_the_scale()
    {
        Assert.Equal(-48, VuMath.TickAngle(0), 6);
        Assert.Equal(0, VuMath.TickAngle(4), 6);
        Assert.Equal(48, VuMath.TickAngle(8), 6);
        Assert.Equal(12, VuMath.TickAngle(5) - VuMath.TickAngle(4), 6);
    }

    [Fact]
    public void Scale_ends_land_on_the_design_arc()
    {
        // Design arc: M28 49.2 A70 70 0 0 1 132 49.2.
        var left = VuMath.PointAt(-48, VuMath.ScaleRadius);
        var right = VuMath.PointAt(48, VuMath.ScaleRadius);
        Assert.Equal(28, left.X, 0);
        Assert.Equal(49.2, left.Y, 0);
        Assert.Equal(132, right.X, 0);
        Assert.Equal(49.2, right.Y, 0);
    }

    [Fact]
    public void Centre_tick_matches_the_design()
    {
        // Design tick: M80 30 L80 22.
        Assert.Equal(new System.Windows.Point(80, 30), VuMath.PointAt(0, VuMath.TickInner));
        Assert.Equal(new System.Windows.Point(80, 22), VuMath.PointAt(0, VuMath.TickOuter));
    }
}

public class KnobMathTests
{
    [Fact]
    public void Six_detents_match_the_design_angles() =>
        Assert.Equal([-125.0, -75, -25, 25, 75, 125], Enumerable.Range(0, 6).Select(i => KnobMath.DetentAngle(i, 6)));

    [Fact]
    public void One_detent_points_straight_up() => Assert.Equal(0, KnobMath.DetentAngle(0, 1));

    [Fact]
    public void A_knob_needs_a_detent() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => KnobMath.DetentAngle(0, 0));

    [Theory]
    [InlineData(-3, -125)]
    [InlineData(9, 125)]
    public void Detent_index_clamps(int index, double angle) => Assert.Equal(angle, KnobMath.DetentAngle(index, 6));

    [Fact]
    public void Labels_sit_on_a_circle_round_the_knob()
    {
        var top = KnobMath.LabelCenter(0);
        Assert.Equal(60, top.X, 6);
        Assert.Equal(12, top.Y, 6);

        var right = KnobMath.LabelCenter(90);
        Assert.Equal(110, right.X, 6);
        Assert.Equal(62, right.Y, 6);
    }

    [Theory]
    [InlineData(5, 1, true, 0)]
    [InlineData(0, -1, true, 5)]
    [InlineData(2, 1, true, 3)]
    [InlineData(5, 1, false, 5)]
    [InlineData(0, -1, false, 0)]
    [InlineData(2, -1, false, 1)]
    public void Steps_wrap_on_click_and_stop_on_keys(int index, int delta, bool wrap, int expected) =>
        Assert.Equal(expected, KnobMath.Step(index, 6, delta, wrap));

    [Fact]
    public void The_recommended_tick_points_out_from_the_center_between_the_skirt_and_the_labels()
    {
        var (inner, outer) = KnobMath.TickEnds(90);
        Assert.Equal(60 + KnobMath.TickInner, inner.X, 6);
        Assert.Equal(62, inner.Y, 6);
        Assert.Equal(60 + KnobMath.TickOuter, outer.X, 6);
        Assert.Equal(62, outer.Y, 6);

        // A two-digit label reaches about 7 units from the label circle, so the tick stops short of it.
        Assert.True(KnobMath.TickInner > KnobMath.SkirtRadius);
        Assert.True(KnobMath.TickOuter <= KnobMath.LabelRadius - 8);
    }

    [Fact]
    public void Stepping_a_knob_without_detents_selects_nothing() => Assert.Equal(-1, KnobMath.Step(0, 0, 1, true));
}

public class LedMathTests
{
    [Theory]
    [InlineData(19 / 30.0, 30, 19)]
    [InlineData(0.6333, 30, 19)]
    [InlineData(0, 30, 0)]
    [InlineData(1, 30, 30)]
    [InlineData(1.5, 30, 30)]
    [InlineData(-1, 30, 0)]
    [InlineData(0.5, 3, 2)]
    [InlineData(0.5, 0, 0)]
    [InlineData(double.NaN, 30, 0)]
    public void Lit_count_rounds_and_clamps(double fraction, int segments, int lit) =>
        Assert.Equal(lit, LedMath.LitCount(fraction, segments));

    [Fact]
    public void Segments_fill_the_width_on_whole_pixels()
    {
        const int n = 30;
        const double width = 396;
        var last = LedMath.SegmentBounds(n - 1, n, width, 3);
        Assert.Equal(width, last.Left + last.Width, 6);

        for (var i = 0; i < n; i++)
        {
            var (left, w) = LedMath.SegmentBounds(i, n, width, 3);
            Assert.Equal(Math.Round(left), left);
            Assert.True(w > 0);
            if (i > 0)
            {
                var prev = LedMath.SegmentBounds(i - 1, n, width, 3);
                Assert.InRange(left - (prev.Left + prev.Width), 2, 4);
            }
        }
    }

    [Fact]
    public void No_segments_have_no_bounds() => Assert.Equal((0.0, 0.0), LedMath.SegmentBounds(0, 0, 100, 3));
}
