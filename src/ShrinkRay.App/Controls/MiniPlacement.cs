using System.Windows;

namespace ShrinkRay.App.Controls;

/// <summary>Where the mini cassette opens. No window here, so tests cover it.</summary>
public static class MiniPlacement
{
    /// <summary>The gap between a fresh mini cassette and the work area's corner.</summary>
    public const double Margin = 16;

    /// <summary>
    /// The top-left corner for a mini cassette of <paramref name="size"/>. A saved position is
    /// kept, pulled back inside <paramref name="screens"/> if a monitor has gone. With none saved,
    /// it sits in the bottom-right corner of <paramref name="workArea"/>, above the tray.
    /// </summary>
    public static Point Place((double Left, double Top)? saved, Size size, Rect workArea, Rect screens)
    {
        if (saved is { } p && !screens.IsEmpty)
        {
            return new Point(
                Clamp(p.Left, screens.Left, screens.Right - size.Width),
                Clamp(p.Top, screens.Top, screens.Bottom - size.Height));
        }

        return new Point(
            Math.Max(workArea.Left, workArea.Right - size.Width - Margin),
            Math.Max(workArea.Top, workArea.Bottom - size.Height - Margin));
    }

    private static double Clamp(double value, double min, double max) => max < min ? min : Math.Clamp(value, min, max);
}
