using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Tests;

public class TapeDeckMathTests
{
    [Fact]
    public void Run_matches_the_deck_design_for_a_full_source_and_empty_output()
    {
        var run = TapeDeckMath.TapeRun(1, 0);

        Assert.Equal(4, run.Length);
        Assert.Equal((140, 218.8), Round(run[0]));
        Assert.Equal((252, 251), Round(run[1]));
        Assert.Equal((388, 251), Round(run[2]));
        Assert.Equal((477, 179.24), Round(run[3]));
    }

    [Fact]
    public void Each_end_moves_in_and_up_as_its_pack_shrinks()
    {
        var full = TapeDeckMath.TapeRun(1, 1);
        var empty = TapeDeckMath.TapeRun(0, 0);

        Assert.True(empty[0].X > full[0].X);
        Assert.True(empty[0].Y < full[0].Y);
        Assert.True(empty[3].X < full[3].X);
        Assert.True(empty[3].Y < full[3].Y);
    }

    [Fact]
    public void Guides_stay_put_whatever_the_packs()
    {
        var a = TapeDeckMath.TapeRun(0.2, 0.7);
        var b = TapeDeckMath.TapeRun(0.9, 0.1);

        Assert.Equal(a[1], b[1]);
        Assert.Equal(a[2], b[2]);
    }

    [Fact]
    public void Reels_sit_mirrored_about_the_centre_of_the_design()
    {
        Assert.Equal(TapeDeckMath.DesignWidth / 2 - TapeDeckMath.SourceHubX, TapeDeckMath.OutputHubX - TapeDeckMath.DesignWidth / 2, 6);
        Assert.True(TapeDeckMath.ReelTop + TapeDeckMath.ReelSize <= TapeDeckMath.DesignHeight);
    }

    [Fact]
    public void Run_ends_leave_from_the_pack_edge()
    {
        var run = TapeDeckMath.TapeRun(0.5, 0.5);
        var radius = ReelMath.PackRadius(0.5);

        Assert.Equal(radius, Distance(run[0], (TapeDeckMath.SourceHubX, TapeDeckMath.HubY)), 0);
        Assert.Equal(radius, Distance(run[3], (TapeDeckMath.OutputHubX, TapeDeckMath.HubY)), 0);
    }

    private static (double, double) Round((double X, double Y) p) => (Math.Round(p.X, 6), Math.Round(p.Y, 6));

    private static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
