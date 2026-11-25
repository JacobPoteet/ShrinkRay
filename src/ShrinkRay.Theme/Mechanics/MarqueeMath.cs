namespace ShrinkRay.Theme.Mechanics;

/// <summary>
/// Timing and geometry of a scrolling title. One cycle waits, scrolls left at a slow steady speed
/// until the end shows, waits again, then fades out, jumps back and fades in. The fade is the
/// calmer return: nothing sweeps backwards across the label.
/// </summary>
public static class MarqueeMath
{
    /// <summary>Scroll speed in device-independent pixels per second.</summary>
    public const double Speed = 28;

    /// <summary>The wait at the start and at the end of the scroll, in seconds.</summary>
    public const double HoldSeconds = 2;

    /// <summary>One half of the return: the fade out, then the fade in, in seconds.</summary>
    public const double FadeSeconds = 0.25;

    /// <summary>Space left after the last letter when the scroll ends.</summary>
    public const double EndPad = 8;

    /// <summary>Width the text must pass the viewport by before it counts as overflowing.</summary>
    public const double Tolerance = 0.5;

    /// <summary>True when the text is wider than the room it has.</summary>
    public static bool Overflows(double textWidth, double viewportWidth) =>
        viewportWidth > 0 && textWidth - viewportWidth > Tolerance;

    /// <summary>How far the text travels left so that its end shows. Zero when it fits.</summary>
    public static double Distance(double textWidth, double viewportWidth) =>
        Overflows(textWidth, viewportWidth) ? textWidth - viewportWidth + EndPad : 0;

    /// <summary>Seconds the scroll itself takes.</summary>
    public static double ScrollSeconds(double distance) => distance <= 0 ? 0 : distance / Speed;

    /// <summary>Seconds in one full cycle. Zero when there is nothing to scroll, so nothing runs.</summary>
    public static double CycleSeconds(double distance) =>
        distance <= 0 ? 0 : HoldSeconds + ScrollSeconds(distance) + HoldSeconds + (2 * FadeSeconds);

    /// <summary>The title's leftward offset and opacity at a moment in the repeating cycle.</summary>
    public static (double Offset, double Opacity) FrameAt(double seconds, double distance)
    {
        var cycle = CycleSeconds(distance);
        if (cycle <= 0)
        {
            return (0, 1);
        }

        var t = seconds % cycle;
        if (t < 0)
        {
            t += cycle;
        }

        var scroll = ScrollSeconds(distance);
        if (t < HoldSeconds)
        {
            return (0, 1);
        }

        t -= HoldSeconds;
        if (t < scroll)
        {
            return (t * Speed, 1);
        }

        t -= scroll;
        if (t < HoldSeconds)
        {
            return (distance, 1);
        }

        t -= HoldSeconds;
        return t < FadeSeconds
            ? (distance, 1 - (t / FadeSeconds))
            : (0, (t - FadeSeconds) / FadeSeconds);
    }
}
