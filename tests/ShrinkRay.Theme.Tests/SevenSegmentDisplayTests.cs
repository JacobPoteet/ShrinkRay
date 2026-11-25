using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShrinkRay.Theme.Controls;

namespace ShrinkRay.Theme.Tests;

public sealed class SevenSegmentDisplayTests
{
    [Fact]
    public void Unlit_segments_do_not_glow() => Sta(() =>
        Assert.Equal(Render("  ", glow: false), Render("  ", glow: true)));

    [Fact]
    public void Lit_segments_glow() => Sta(() =>
        Assert.NotEqual(Render("18", glow: false), Render("18", glow: true)));

    /// <summary>The themed display in a margin wide enough to catch any glow around it.</summary>
    private static byte[] Render(string text, bool glow)
    {
        // Touching Application registers the pack: scheme the theme loads itself through.
        _ = Application.Current;
        var theme = new ResourceDictionary();
        theme.MergedDictionaries.Add(new ShrinkRayTheme());
        var host = new Border
        {
            Resources = theme,
            Padding = new Thickness(20),
            Child = new SevenSegmentDisplay { Text = text, DigitHeight = 40, Glow = glow },
        };
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();

        var width = (int)host.ActualWidth;
        var height = (int)host.ActualHeight;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        Assert.Contains(pixels, b => b != 0);
        return pixels;
    }

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
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
