using ShrinkRay.Core.Swap;

namespace ShrinkRay.Core.Library;

/// <summary>Where an episode stands between scans.</summary>
public enum EpisodeStatus
{
    /// <summary>Eligible and waiting to be encoded.</summary>
    Queued,

    /// <summary>ShrinkRay replaced it.</summary>
    Shrunk,

    /// <summary>Encoded and checked, but it saved too little, so the original stayed.</summary>
    Kept,

    /// <summary>The last attempt failed. Encode tries it again.</summary>
    Failed,

    /// <summary>Not eligible: already HEVC, another codec, or unreadable.</summary>
    Skipped,
}

public static class EpisodeStatusRules
{
    /// <param name="journalState">The swap journal's latest state for the file.</param>
    /// <param name="inLedger">True when the ledger records ShrinkRay replacing the file.</param>
    public static EpisodeStatus Classify(EpisodeRecord episode, SwapState? journalState, bool inLedger)
    {
        if (!episode.IsEligible)
        {
            // An HEVC file ShrinkRay wrote counts as shrunk; any other ineligible file is skipped.
            return inLedger || journalState == SwapState.OriginalDeleted ? EpisodeStatus.Shrunk : EpisodeStatus.Skipped;
        }

        return journalState switch
        {
            SwapState.KeptOriginal => EpisodeStatus.Kept,
            SwapState.Failed => EpisodeStatus.Failed,

            // Skip or Stop is the user's call, not a failure. The file waits for the next run.
            SwapState.Cancelled => EpisodeStatus.Queued,
            _ => EpisodeStatus.Queued,
        };
    }

    /// <summary>Queued and failed episodes go on the next run.</summary>
    public static bool IsQueueable(this EpisodeStatus status) => status is EpisodeStatus.Queued or EpisodeStatus.Failed;
}

/// <summary>Why a show is left off the candidate list.</summary>
public enum HiddenReason
{
    None,

    /// <summary>ShrinkRay has shrunk everything it can.</summary>
    Shrunk,

    /// <summary>Everything is already HEVC, AV1 or VP9.</summary>
    Hevc,

    /// <summary>Nothing it can encode for another reason, such as an unsupported codec.</summary>
    Other,
}

/// <summary>One show's totals for the show list.</summary>
public sealed record ShowSummary(
    string Name,
    string Folder,
    int SeasonCount,
    int EpisodeCount,
    long TotalBytes,
    int QueueableCount,
    long QueueableBytes,
    long QueueableFrames,
    int ShrunkCount,
    int EfficientCount,
    string CodecLabel)
{
    public bool IsCandidate => QueueableCount > 0;

    public HiddenReason Hidden =>
        IsCandidate ? HiddenReason.None
        : ShrunkCount > 0 ? HiddenReason.Shrunk
        : EfficientCount > 0 ? HiddenReason.Hevc
        : HiddenReason.Other;

    /// <summary>Bytes the queueable episodes would give back at <paramref name="cq"/>.</summary>
    public long ProjectedSavings(int cq) => (long)(QueueableBytes * (1 - SizeEstimate.RatioFor(cq)));

    public static ShowSummary From(ShowScan scan, Func<EpisodeRecord, EpisodeStatus> classify)
    {
        var statuses = scan.Episodes.Select(e => (Episode: e, Status: classify(e))).ToList();
        var queueable = statuses.Where(s => s.Status.IsQueueable()).Select(s => s.Episode).ToList();
        var codecs = scan.Episodes
            .Select(e => e.VideoCodec)
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ShowSummary(
            scan.Name,
            scan.Folder,
            scan.Episodes.Select(e => e.Season).Where(s => s > 0).Distinct().Count(),
            scan.Episodes.Count,
            scan.Episodes.Sum(e => e.SizeBytes),
            queueable.Count,
            queueable.Sum(e => e.SizeBytes),
            queueable.Sum(e => e.FrameCount ?? 0),
            statuses.Count(s => s.Status == EpisodeStatus.Shrunk),
            scan.Episodes.Count(e => e.IsEfficientCodec),
            LabelFor(codecs));
    }

    private static string LabelFor(IReadOnlyList<string> codecs) => codecs.Count switch
    {
        0 => "NO VIDEO",
        1 => CodecName(codecs[0]),
        _ => "MIXED",
    };

    /// <summary>The way the deck prints a codec, such as <c>H.264</c>.</summary>
    public static string CodecName(string codec) => codec.ToLowerInvariant() switch
    {
        "h264" => "H.264",
        "hevc" => "HEVC",
        "" => "UNKNOWN",
        var other => other.ToUpperInvariant(),
    };
}
