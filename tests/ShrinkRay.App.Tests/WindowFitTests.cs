using System.Windows;
using ShrinkRay.App.Controls;

namespace ShrinkRay.App.Tests;

public class WindowFitTests
{
    private static readonly Size Design = new(1440, 900);

    [Theory]
    [InlineData(0, 0, 2560, 1392, 1)]
    [InlineData(0, 0, 2560, 1392, 1.5)]
    [InlineData(-2560, 0, 3840, 2112, 2)]
    public void A_window_at_the_design_size_stays_where_the_user_put_it(double x, double y, double width, double height, double dpi)
    {
        // Half off the edge of the screen is still the user's choice.
        var window = new Rect(x - 400, y + 100, 1440 * dpi, 900 * dpi);

        Assert.Null(WindowFit.Refit(window, new Rect(x, y, width, height), Design, dpi));
    }

    [Fact]
    public void A_pixel_of_rounding_is_not_worth_a_move()
    {
        var work = new Rect(0, 0, 2560, 1392);

        Assert.Null(WindowFit.Refit(new Rect(10, 10, 1441, 899), work, Design, 1));
    }

    [Fact]
    public void A_narrow_screen_gets_a_smaller_window_of_the_same_shape_inside_it()
    {
        // A portrait monitor to the right of the main one.
        var work = new Rect(2560, -255, 1080, 1872);

        var fit = WindowFit.Refit(new Rect(2500, 200, 1440, 900), work, Design, 1);

        Assert.Equal(new Rect(2560, 312, 1080, 675), fit);
    }

    [Fact]
    public void A_short_screen_is_measured_in_scaled_pixels()
    {
        // 1920 by 1080 at 150 percent leaves 1280 by 672 to draw in.
        var work = new Rect(0, 0, 1920, 1008);

        var fit = WindowFit.Refit(new Rect(-120, -171, 2160, 1350), work, Design, 1.5);

        Assert.Equal(new Rect(154, 0, 1613, 1008), fit);
    }

    [Fact]
    public void A_shrunken_window_grows_back_around_its_center_on_a_bigger_screen()
    {
        var work = new Rect(0, 0, 2560, 1392);

        var fit = WindowFit.Refit(new Rect(600, 300, 1080, 675), work, Design, 1);

        Assert.Equal(new Rect(420, 188, 1440, 900), fit);
    }

    [Fact]
    public void Growing_back_near_an_edge_stays_on_the_screen()
    {
        var work = new Rect(0, 0, 2560, 1392);

        var fit = WindowFit.Refit(new Rect(1900, 800, 1080, 675), work, Design, 1);

        Assert.Equal(new Rect(1120, 492, 1440, 900), fit);
    }
}
