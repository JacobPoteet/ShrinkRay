using System.Windows;

namespace ShrinkRay.App.Controls;

/// <summary>
/// The window has one size, the deck's design size, and the user cannot change it. A screen too
/// small for it gets the largest window of the same shape, which <see cref="DeckScaler"/> draws
/// the deck into.
/// </summary>
public static class WindowFit
{
    /// <summary>
    /// Where <paramref name="window"/> goes on a screen with this <paramref name="workArea"/>, or
    /// null when its size is already right and it can stay where it is. A window that changes
    /// size keeps its center, pushed inside the work area. Both rectangles are in screen pixels.
    /// </summary>
    public static Rect? Refit(Rect window, Rect workArea, Size design, double dpiScale)
    {
        var scale = DeckScaler.ScaleFor(new Size(workArea.Width / dpiScale, workArea.Height / dpiScale), design);
        var width = Math.Round(design.Width * scale * dpiScale);
        var height = Math.Round(design.Height * scale * dpiScale);

        // A pixel either way is rounding, and correcting it would only move the window about.
        if (Math.Abs(window.Width - width) <= 1 && Math.Abs(window.Height - height) <= 1)
        {
            return null;
        }

        var left = Inside(window.X + ((window.Width - width) / 2), workArea.Left, workArea.Right - width);
        var top = Inside(window.Y + ((window.Height - height) / 2), workArea.Top, workArea.Bottom - height);
        return new Rect(left, top, width, height);
    }

    private static double Inside(double value, double min, double max) => Math.Max(min, Math.Min(Math.Round(value), max));
}
