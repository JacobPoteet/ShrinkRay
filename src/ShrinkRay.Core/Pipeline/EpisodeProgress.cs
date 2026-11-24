using ShrinkRay.Core.Encode;

namespace ShrinkRay.Core.Pipeline;

/// <summary>The step an episode is on, in the order <see cref="EpisodePipeline"/> runs them.</summary>
public enum EpisodePhase
{
    /// <summary>ffprobe reads the original on the share.</summary>
    Probing,

    /// <summary>The original is copied from the share to the scratch folder.</summary>
    Copying,

    /// <summary>HandBrakeCLI encodes the scratch copy.</summary>
    Encoding,

    /// <summary>ffmpeg copies font attachments the encode dropped back in.</summary>
    RestoringFonts,

    /// <summary>The full decode check and the comparison with the source.</summary>
    Verifying,

    /// <summary>The verified file is copied to the share beside the original under a temp name.</summary>
    Uploading,

    /// <summary>The renames that put the new file under the original name, then the backup delete.</summary>
    Swapping,
}

public static class EpisodePhases
{
    /// <summary>
    /// True once the swap has renamed the original aside. From there the swap ignores
    /// cancellation and runs to the end, so the deck lets it finish instead of cancelling it. Up
    /// to and through the upload a cancel is safe: the temp file is removed and the original was
    /// never touched.
    /// </summary>
    public static bool IsPastUpload(this EpisodePhase phase) => phase >= EpisodePhase.Swapping;
}

/// <param name="Encode">HandBrake's progress. Set only during <see cref="EpisodePhase.Encoding"/>, once HandBrake reports.</param>
/// <param name="Fraction">
/// How much of the file has moved, 0 to 1. Set only during <see cref="EpisodePhase.Copying"/> and
/// <see cref="EpisodePhase.Uploading"/>, once bytes move.
/// </param>
public sealed record EpisodeProgress(EpisodePhase Phase, EncodeProgress? Encode = null, double? Fraction = null)
{
    /// <summary>A copy or upload report: <paramref name="bytes"/> of <paramref name="total"/> have moved.</summary>
    public static EpisodeProgress Transfer(EpisodePhase phase, long bytes, long total) =>
        new(phase, Fraction: total > 0 ? Math.Clamp((double)bytes / total, 0, 1) : 1);
}

/// <summary>Reports on the caller's thread, unlike <see cref="Progress{T}"/>, which posts and can reorder.</summary>
internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
