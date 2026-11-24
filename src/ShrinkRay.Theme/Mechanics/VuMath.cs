using System.Windows;

namespace ShrinkRay.Theme.Mechanics;

/// <summary>
/// VU meter geometry in the design's 160 by 100 face: the needle pivots at (80, 96) and sweeps
/// 48 degrees either side of vertical. Angles are degrees clockwise from straight up.
/// </summary>
public static class VuMath
{
    public const double Sweep = 48;
    public static readonly Point Pivot = new(80, 96);
    public const double ScaleRadius = 70;
    public const double TickInner = 66;
    public const double TickOuter = 74;
    public const double NeedleLength = 70;
    public const int TickCount = 9;

    /// <summary>The value as a fraction of the range, clamped to 0..1. An empty range reads 0.</summary>
    public static double Fraction(double value, double minimum, double maximum)
    {
        if (maximum <= minimum || double.IsNaN(value))
        {
            return 0;
        }

        return System.Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
    }

    /// <summary>Needle angle for <paramref name="value"/>: -48 at the minimum, +48 at the maximum.</summary>
    public static double NeedleAngle(double value, double minimum, double maximum) =>
        -Sweep + Fraction(value, minimum, maximum) * 2 * Sweep;

    /// <summary>Angle of tick <paramref name="index"/> of <see cref="TickCount"/>.</summary>
    public static double TickAngle(int index) => -Sweep + index * (2 * Sweep / (TickCount - 1));

    /// <summary>The point at <paramref name="radius"/> along <paramref name="angle"/> from the pivot.</summary>
    public static Point PointAt(double angle, double radius)
    {
        var rad = angle * System.Math.PI / 180;
        return new Point(Pivot.X + radius * System.Math.Sin(rad), Pivot.Y - radius * System.Math.Cos(rad));
    }
}
