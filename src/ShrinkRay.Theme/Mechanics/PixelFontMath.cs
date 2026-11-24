namespace ShrinkRay.Theme.Mechanics;

/// <summary>
/// scientifica is a bitmap font drawn on an 11 pixel em. It only stays crisp when each font
/// pixel lands on a whole number of screen pixels, so the size in device pixels must be a
/// multiple of 11.
/// </summary>
public static class PixelFontMath
{
    /// <summary>The em size the font was drawn at, in pixels.</summary>
    public const double Cell = 11;

    /// <summary>
    /// Returns the font size in DIPs closest to <paramref name="desiredDip"/> whose device
    /// size is a whole multiple of <see cref="Cell"/>. At 100% scale, 22 stays 22.
    /// </summary>
    public static double Snap(double desiredDip, double pixelsPerDip)
    {
        if (desiredDip <= 0 || pixelsPerDip <= 0 || double.IsNaN(desiredDip) || double.IsNaN(pixelsPerDip))
        {
            throw new ArgumentOutOfRangeException(nameof(desiredDip), "Size and scale must be positive.");
        }

        var multiple = Math.Max(1, Math.Round(desiredDip * pixelsPerDip / Cell, MidpointRounding.AwayFromZero));
        return multiple * Cell / pixelsPerDip;
    }
}
