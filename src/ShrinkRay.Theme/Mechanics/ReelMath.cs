namespace ShrinkRay.Theme.Mechanics;

/// <summary>
/// Reel geometry in the units of the design's deck: flange radius 84, hub radius 30, and a tape
/// pack that runs from radius 34 (empty) to 80 (full).
/// </summary>
public static class ReelMath
{
    public const double FlangeRadius = 84;
    public const double HubRadius = 30;
    public const double EmptyPackRadius = 34;
    public const double FullPackRadius = 80;

    /// <summary>Pack radius for a fill between 0 and 1. Values outside clamp.</summary>
    public static double PackRadius(double fill) =>
        EmptyPackRadius + (FullPackRadius - EmptyPackRadius) * Clamp01(fill);

    /// <summary>
    /// Fill of the source reel as an episode encodes: full at progress 0, empty at 1.
    /// </summary>
    public static double SourceFill(double progress) => 1 - Clamp01(progress);

    /// <summary>
    /// Fill of the output reel: it grows to <paramref name="ratio"/> of the source, the
    /// expected output size over the source size, so it ends smaller.
    /// </summary>
    public static double OutputFill(double progress, double ratio) => Clamp01(progress) * Clamp01(ratio);

    /// <summary>Scale from design units to an element of <paramref name="diameter"/> pixels.</summary>
    public static double Scale(double diameter) => diameter <= 0 ? 0 : diameter / (2 * FlangeRadius);

    internal static double Clamp01(double v) => double.IsNaN(v) ? 0 : System.Math.Clamp(v, 0, 1);
}
