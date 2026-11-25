namespace ShrinkRay.Theme.Mechanics;

/// <summary>
/// A CRT warming up, as progress runs from 0 to 1: a bright line sweeps out from the middle of
/// the tube, then the picture opens from that line to its full height while the line fades.
/// At 1 the screen is at rest.
/// </summary>
public static class WarmUpMath
{
    /// <summary>How long one warm-up takes.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(450);

    /// <summary>The progress at which the line reaches full width and the picture starts to open.</summary>
    public const double LineDrawn = 0.35;

    /// <summary>The line's width when it first appears, as a fraction of the screen.</summary>
    public const double StartWidth = 0.12;

    /// <summary>The line's width, 0 to 1 of the screen.</summary>
    public static double LineWidth(double progress) =>
        StartWidth + ((1 - StartWidth) * EaseOut(progress / LineDrawn));

    /// <summary>The line's brightness, 0 to 1: full while it draws, then fading to nothing.</summary>
    public static double LineOpacity(double progress) =>
        progress >= 1 ? 0 : 1 - Math.Clamp((progress - LineDrawn) / (1 - LineDrawn), 0, 1);

    /// <summary>The picture's height, 0 to 1 of the screen. 0 until the line is drawn.</summary>
    public static double Opening(double progress) =>
        progress >= 1 ? 1 : EaseOut((progress - LineDrawn) / (1 - LineDrawn));

    private static double EaseOut(double t)
    {
        var left = 1 - Math.Clamp(t, 0, 1);
        return 1 - (left * left * left);
    }
}
