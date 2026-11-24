namespace ShrinkRay.Core.Library;

/// <summary>
/// Expected output size over source size at a CQ, and encode time from frame counts. The
/// anchors are the mean of four 3-minute clips encoded with nvenc_h265_10bit on an RTX 2070
/// SUPER: CQ 22 kept 73% of the size, CQ 25 kept 50.5%, CQ 28 kept 36%, all at about 165 fps.
/// </summary>
public static class SizeEstimate
{
    /// <summary>Frames per second the measured card held at every CQ.</summary>
    public const double MeasuredFps = 165;

    private static readonly (int Cq, double Ratio)[] Anchors = [(22, 0.73), (25, 0.505), (28, 0.36)];

    /// <summary>Output size over source size. Interpolates between anchors and extends the end slopes.</summary>
    public static double RatioFor(int cq)
    {
        var (lowCq, lowRatio) = Anchors[0];
        var (highCq, highRatio) = Anchors[1];
        for (var i = 1; i < Anchors.Length; i++)
        {
            (lowCq, lowRatio) = Anchors[i - 1];
            (highCq, highRatio) = Anchors[i];
            if (cq <= highCq)
            {
                break;
            }
        }

        var ratio = lowRatio + (highRatio - lowRatio) * (cq - lowCq) / (highCq - lowCq);
        return Math.Clamp(ratio, 0.15, 0.95);
    }

    /// <summary>The fraction an encode is expected to save, such as 0.64 at CQ 28.</summary>
    public static double SavingsFor(int cq) => 1 - RatioFor(cq);

    /// <summary>Encode time for a frame count at a speed in frames per second.</summary>
    public static TimeSpan EncodeTime(long frames, double fps = MeasuredFps) =>
        frames <= 0 || fps <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(frames / fps);
}
