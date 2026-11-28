namespace ShrinkRay.Theme.Mechanics;

/// <summary>
/// The cassette's layout in its 640 by 256 design space: where the reels sit, and the four
/// points of the tape run from the source pack, past the guides and the head, to the output pack.
/// </summary>
public static class TapeDeckMath
{
    public const double DesignWidth = 640;
    public const double DesignHeight = 256;

    /// <summary>Both reels are drawn this size, with their flanges touching the window's top and bottom.</summary>
    public const double ReelSize = 2 * ReelMath.FlangeRadius;

    public const double ReelTop = 66;
    public const double SourceReelLeft = 96;
    public const double OutputReelLeft = 376;

    /// <summary>The source reel turns slower than the output reel, as a real deck's supply side does.</summary>
    public const double SourceSecondsPerTurn = 3.2;

    public const double OutputSecondsPerTurn = 2.1;

    public static double SourceHubX => SourceReelLeft + ReelMath.FlangeRadius;

    public static double OutputHubX => OutputReelLeft + ReelMath.FlangeRadius;

    public static double HubY => ReelTop + ReelMath.FlangeRadius;

    /// <summary>
    /// The four points of the run for two pack fills. Each end leaves its pack at the lower
    /// outside of the reel, so it moves in and up as the pack shrinks.
    /// </summary>
    public static (double X, double Y)[] TapeRun(double sourcePack, double outputPack)
    {
        var left = ReelMath.PackRadius(sourcePack);
        var right = ReelMath.PackRadius(outputPack);
        return
        [
            (SourceHubX - left * 0.5, HubY + left * 0.86),
            (252, 251),
            (388, 251),
            (OutputHubX + right * 0.5, HubY + right * 0.86),
        ];
    }
}
