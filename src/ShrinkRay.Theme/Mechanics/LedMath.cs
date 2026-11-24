namespace ShrinkRay.Theme.Mechanics;

/// <summary>LED bar arithmetic.</summary>
public static class LedMath
{
    /// <summary>
    /// How many of <paramref name="segments"/> light for <paramref name="fraction"/>. Rounds to
    /// the nearest segment and clamps to 0..segments.
    /// </summary>
    public static int LitCount(double fraction, int segments)
    {
        if (segments <= 0 || double.IsNaN(fraction))
        {
            return 0;
        }

        var lit = (int)System.Math.Round(System.Math.Clamp(fraction, 0, 1) * segments, MidpointRounding.AwayFromZero);
        return System.Math.Clamp(lit, 0, segments);
    }

    /// <summary>
    /// Left edge and width of segment <paramref name="index"/> when <paramref name="segments"/>
    /// share <paramref name="width"/> with <paramref name="gap"/> between them. Edges snap to
    /// whole pixels so every gap looks the same.
    /// </summary>
    public static (double Left, double Width) SegmentBounds(int index, int segments, double width, double gap)
    {
        if (segments <= 0)
        {
            return (0, 0);
        }

        var each = (width - gap * (segments - 1)) / segments;
        var left = System.Math.Round(index * (each + gap));
        var right = System.Math.Round(index * (each + gap) + each);
        return (left, System.Math.Max(0, right - left));
    }
}
