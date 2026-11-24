namespace ShrinkRay.Core.Library;

/// <summary>What a scan learned about one episode file.</summary>
/// <param name="Season">The season number; 0 for specials and files whose season is unknown.</param>
/// <param name="Code">Such as <c>S01E03</c>, or the file name when it carries no episode number.</param>
/// <param name="VideoCodec">ffprobe's codec name for the main video stream, such as <c>h264</c>.</param>
/// <param name="SkipMessage">Why the file cannot be encoded. Empty when it can.</param>
public sealed record EpisodeRecord(
    string Path,
    int Season,
    int? Episode,
    string Code,
    string Title,
    long SizeBytes,
    double? DurationSeconds,
    double? FrameRate,
    string VideoCodec,
    bool IsEligible,
    string SkipMessage)
{
    /// <summary>Frames to encode, from duration and frame rate. Null when either is unknown.</summary>
    public long? FrameCount => DurationSeconds is { } d && FrameRate is { } r && d > 0 && r > 0
        ? (long)Math.Round(d * r)
        : null;

    /// <summary>True when the main video stream is already HEVC, AV1 or VP9.</summary>
    public bool IsEfficientCodec => VideoCodec is "hevc" or "av1" or "vp9";
}

/// <summary>Every episode file found under one show folder, in season and episode order.</summary>
public sealed record ShowScan(string Name, string Folder, IReadOnlyList<EpisodeRecord> Episodes);
