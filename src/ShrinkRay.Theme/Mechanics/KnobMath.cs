using System.Windows;

namespace ShrinkRay.Theme.Mechanics;

/// <summary>
/// Rotary knob geometry in the design's 120 by 112 box: the knob turns about (60, 62) and its
/// detents spread across 250 degrees, so six detents sit 50 degrees apart from -125 to +125.
/// Angles are degrees clockwise from straight up.
/// </summary>
public static class KnobMath
{
    public const double Sweep = 250;
    public static readonly Point Center = new(60, 62);
    public const double LabelRadius = 50;

    /// <summary>Outer edge of the knurled skirt, which the knob's body draws at radius 36.</summary>
    public const double SkirtRadius = 36;

    /// <summary>The recommended-detent tick runs between these radii: clear of the skirt and inside the labels.</summary>
    public const double TickInner = 37.5;

    /// <summary>A label's text reaches about 7 units either side of its center, so the tick stops 8 short of the label circle.</summary>
    public const double TickOuter = 41.5;

    /// <summary>Angle of detent <paramref name="index"/> out of <paramref name="count"/>. One detent points up.</summary>
    public static double DetentAngle(int index, int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "A knob needs at least one detent.");
        }

        if (count == 1)
        {
            return 0;
        }

        var i = System.Math.Clamp(index, 0, count - 1);
        return -Sweep / 2 + i * (Sweep / (count - 1));
    }

    /// <summary>Where the label for a detent at <paramref name="angle"/> centers.</summary>
    public static Point LabelCenter(double angle, double radius = LabelRadius)
    {
        var rad = angle * System.Math.PI / 180;
        return new Point(Center.X + radius * System.Math.Sin(rad), Center.Y - radius * System.Math.Cos(rad));
    }

    /// <summary>The two ends of the radial tick that marks a detent at <paramref name="angle"/>, inner first.</summary>
    public static (Point Inner, Point Outer) TickEnds(double angle) =>
        (LabelCenter(angle, TickInner), LabelCenter(angle, TickOuter));

    /// <summary>Next index for one step. Clicks wrap around; arrow keys stop at the ends.</summary>
    public static int Step(int index, int count, int delta, bool wrap)
    {
        if (count <= 0)
        {
            return -1;
        }

        var next = index + delta;
        if (wrap)
        {
            return ((next % count) + count) % count;
        }

        return System.Math.Clamp(next, 0, count - 1);
    }
}
