using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ShrinkRay.App.Controls;

namespace ShrinkRay.App.Tests;

public class DeckScalerTests
{
    private static readonly Size Design = new(1440, 900);

    [Theory]
    [InlineData(1440, 900, 1)]
    [InlineData(1920, 1080, 1)]
    [InlineData(2560, 1400, 1)]
    [InlineData(1280, 720, 0.8)]
    [InlineData(1536, 830, 830.0 / 900)]
    [InlineData(1024, 900, 1024.0 / 1440)]
    public void Scale_shrinks_only_below_the_design_size(double width, double height, double expected)
    {
        Assert.Equal(expected, DeckScaler.ScaleFor(new Size(width, height), Design), 6);
    }

    [Fact]
    public void Unbounded_or_empty_space_leaves_the_scale_at_one()
    {
        Assert.Equal(1, DeckScaler.ScaleFor(new Size(double.PositiveInfinity, double.PositiveInfinity), Design));
        Assert.Equal(1, DeckScaler.ScaleFor(new Size(0, 0), Design));
    }

    [Fact]
    public void At_design_size_or_larger_the_deck_lays_out_untouched()
    {
        OnSta(() =>
        {
            var (scaler, deck) = Arranged(1920, 1080);

            Assert.Equal(1, scaler.Scale);
            Assert.Equal(new Size(1920, 1080), deck.RenderSize);
            Assert.True(deck.RenderTransform.Value.IsIdentity);
        });
    }

    [Fact]
    public void A_smaller_window_scales_the_deck_and_fills_it()
    {
        OnSta(() =>
        {
            var (scaler, deck) = Arranged(1280, 720);

            Assert.Equal(0.8, scaler.Scale, 6);
            // Laid out at 1600 by 900, drawn at 1280 by 720: no clipping and no empty band.
            Assert.Equal(1600, deck.RenderSize.Width, 6);
            Assert.Equal(900, deck.RenderSize.Height, 6);
            Assert.Equal(0.8, deck.RenderTransform.Value.M11, 6);
            Assert.Equal(0.8, deck.RenderTransform.Value.M22, 6);
        });
    }

    private static (DeckScaler Scaler, FrameworkElement Deck) Arranged(double width, double height)
    {
        var deck = new Border { Background = Brushes.Black };
        var scaler = new DeckScaler { Child = deck };
        scaler.Measure(new Size(width, height));
        scaler.Arrange(new Rect(0, 0, width, height));
        return (scaler, deck);
    }

    private static void OnSta(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
