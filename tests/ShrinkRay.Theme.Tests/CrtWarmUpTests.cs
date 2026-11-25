using System.Windows;
using System.Windows.Controls;
using ShrinkRay.Theme.Controls;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Tests;

public sealed class CrtWarmUpTests
{
    [Fact]
    public void The_warm_up_is_under_half_a_second() =>
        Assert.True(WarmUpMath.Duration < TimeSpan.FromMilliseconds(500));

    [Fact]
    public void A_cold_screen_shows_a_short_bright_line_and_no_picture()
    {
        Assert.Equal(0, WarmUpMath.Opening(0));
        Assert.Equal(1, WarmUpMath.LineOpacity(0));
        Assert.Equal(WarmUpMath.StartWidth, WarmUpMath.LineWidth(0), 6);
    }

    [Fact]
    public void The_line_reaches_full_width_before_the_picture_opens()
    {
        Assert.Equal(1, WarmUpMath.LineWidth(WarmUpMath.LineDrawn), 6);
        Assert.Equal(0, WarmUpMath.Opening(WarmUpMath.LineDrawn), 6);
        Assert.Equal(1, WarmUpMath.LineOpacity(WarmUpMath.LineDrawn), 6);
    }

    [Fact]
    public void The_picture_only_opens_and_the_line_only_fades()
    {
        for (var i = 1; i <= 100; i++)
        {
            var (before, now) = ((i - 1) / 100.0, i / 100.0);
            Assert.True(WarmUpMath.Opening(now) >= WarmUpMath.Opening(before));
            Assert.True(WarmUpMath.LineOpacity(now) <= WarmUpMath.LineOpacity(before));
            Assert.True(WarmUpMath.LineWidth(now) >= WarmUpMath.LineWidth(before));
        }
    }

    [Fact]
    public void A_warm_screen_is_at_rest()
    {
        Assert.Equal(1, WarmUpMath.Opening(1));
        Assert.Equal(0, WarmUpMath.LineOpacity(1));
        Assert.Equal(1, WarmUpMath.LineWidth(1), 6);
    }

    [Fact]
    public void Screens_are_the_crt_panels_and_digit_readouts_only() => Sta(() =>
    {
        var panel = new CrtPanel();
        var digits = new SevenSegmentDisplay { Text = "12" };
        var root = new Grid();
        root.Children.Add(panel);
        root.Children.Add(new Border { Child = digits });
        root.Children.Add(new LedBar());
        root.Children.Add(new VuMeter());
        root.Children.Add(new CassetteLabel());

        Assert.Equal(new FrameworkElement[] { panel, digits }, CrtWarmUp.Screens(root));
    });

    [Fact]
    public void Play_leaves_every_screen_at_rest_while_motion_is_held() => Sta(() =>
    {
        var digits = new SevenSegmentDisplay { Text = "12" };
        var before = MotionPolicy.Override;
        try
        {
            MotionPolicy.Override = false;
            CrtWarmUp.Play(digits);

            Assert.Equal(1, CrtWarmUp.GetProgress(digits));
            Assert.False(digits.HasAnimatedProperties);
        }
        finally
        {
            MotionPolicy.Override = before;
        }
    });

    [Fact]
    public void Play_animates_each_screen_when_motion_is_on() => Sta(() =>
    {
        var digits = new SevenSegmentDisplay { Text = "12" };
        var before = MotionPolicy.Override;
        try
        {
            MotionPolicy.Override = true;
            CrtWarmUp.Play(digits);

            Assert.True(digits.HasAnimatedProperties);
        }
        finally
        {
            MotionPolicy.Override = before;
        }
    });

    private static void Sta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException(failure.Message, failure);
        }
    }
}
