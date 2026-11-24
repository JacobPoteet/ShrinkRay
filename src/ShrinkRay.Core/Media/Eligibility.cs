namespace ShrinkRay.Core.Media;

public enum SkipReason
{
    NoVideoStream,
    AlreadyEfficientCodec,
    UnsupportedContainer,
    UnsupportedVideoCodec,
    MultipleVideoStreams,
}

/// <summary>Whether a file should be encoded, and if not, why.</summary>
public sealed record EligibilityResult
{
    public bool IsEligible { get; private init; }
    public ContainerKind Container { get; private init; }
    public SkipReason? Reason { get; private init; }

    /// <summary>A short sentence for the UI and the log.</summary>
    public string Message { get; private init; } = "";

    public static EligibilityResult Eligible(ContainerKind container) =>
        new() { IsEligible = true, Container = container, Message = "Eligible" };

    public static EligibilityResult Skipped(SkipReason reason, string message, ContainerKind container = ContainerKind.Unsupported) =>
        new() { IsEligible = false, Reason = reason, Message = message, Container = container };
}

public static class Eligibility
{
    private static readonly string[] EfficientCodecs = ["hevc", "av1", "vp9"];

    public static EligibilityResult Evaluate(MediaInfo info)
    {
        var video = info.VideoStreams.ToList();
        if (video.Count == 0)
        {
            return EligibilityResult.Skipped(SkipReason.NoVideoStream, "No video stream");
        }

        var codec = (video[0].CodecName ?? "").ToLowerInvariant();
        if (EfficientCodecs.Contains(codec))
        {
            return EligibilityResult.Skipped(SkipReason.AlreadyEfficientCodec, $"Already {codec}");
        }

        var container = Container.Detect(info);
        if (container == ContainerKind.Unsupported)
        {
            return EligibilityResult.Skipped(SkipReason.UnsupportedContainer, "Unsupported container");
        }

        if (codec != "h264")
        {
            var shown = codec.Length == 0 ? "unknown" : codec;
            return EligibilityResult.Skipped(SkipReason.UnsupportedVideoCodec, $"Unsupported video codec ({shown})", container);
        }

        // HandBrake encodes one video track and drops the rest, which verification would reject.
        if (video.Count > 1)
        {
            return EligibilityResult.Skipped(SkipReason.MultipleVideoStreams, $"{video.Count} video streams", container);
        }

        return EligibilityResult.Eligible(container);
    }
}
