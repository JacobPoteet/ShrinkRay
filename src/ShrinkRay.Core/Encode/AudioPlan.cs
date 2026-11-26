using ShrinkRay.Core.Media;

namespace ShrinkRay.Core.Encode;

public enum AudioAction
{
    Passthrough,
    EncodeAac,
}

/// <summary>What HandBrake does with one source audio track.</summary>
/// <param name="TrackNumber">HandBrake's 1-based audio track number.</param>
/// <param name="Encoder">The <c>--aencoder</c> value, such as <c>copy:ac3</c> or <c>av_aac</c>.</param>
/// <param name="BitrateKbps">The <c>--ab</c> value. HandBrake ignores it for passthrough but uses it
/// if a passthrough falls back to AAC.</param>
/// <param name="Mixdown">The <c>--mixdown</c> value, matched to the source channel count.</param>
public sealed record AudioTrackPlan(
    int TrackNumber,
    int StreamIndex,
    string SourceCodec,
    AudioAction Action,
    string Encoder,
    int BitrateKbps,
    string Mixdown);

public static class AudioPlanner
{
    // Lossy codecs HandBrake can copy as-is. DTS copies as its core, which drops any HD layer.
    private static readonly Dictionary<string, string> PassthroughEncoders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aac"] = "copy:aac",
        ["ac3"] = "copy:ac3",
        ["eac3"] = "copy:eac3",
        ["dts"] = "copy:dts",
        ["mp3"] = "copy:mp3",
    };

    public const string AacEncoder = "av_aac";

    /// <param name="keepLossless">Copy lossless tracks as they are instead of turning them into AAC.</param>
    public static IReadOnlyList<AudioTrackPlan> Plan(MediaInfo source, bool keepLossless = false)
    {
        var plans = new List<AudioTrackPlan>();
        var trackNumber = 0;
        foreach (var stream in source.AudioStreams)
        {
            trackNumber++;
            var codec = stream.CodecName ?? "";
            var channels = stream.Channels is > 0 ? stream.Channels.Value : 2;
            var (action, encoder) = Choose(codec, stream.Profile, keepLossless);
            plans.Add(new AudioTrackPlan(
                trackNumber,
                stream.Index,
                codec,
                action,
                encoder,
                AacBitrateKbps(channels),
                Mixdown(channels)));
        }

        return plans;
    }

    /// <summary>
    /// Copies the lossy codecs HandBrake can pass through. Everything else, lossless tracks
    /// (flac, alac, pcm_*, truehd, DTS-HD MA) and lossy codecs HandBrake cannot copy, becomes
    /// AAC. With <paramref name="keepLossless"/> the lossless tracks are copied too.
    /// </summary>
    public static (AudioAction Action, string Encoder) Choose(string codec, string? profile, bool keepLossless = false)
    {
        if (keepLossless && LosslessCopy(codec, profile) is { } lossless)
        {
            return (AudioAction.Passthrough, lossless);
        }

        if (IsDtsHdMa(codec, profile))
        {
            return (AudioAction.EncodeAac, AacEncoder);
        }

        return PassthroughEncoders.TryGetValue(codec, out var copy)
            ? (AudioAction.Passthrough, copy)
            : (AudioAction.EncodeAac, AacEncoder);
    }

    /// <summary>AAC bitrate by source channel count: 96 mono, 192 stereo, 384 for 5.1, 448 for 6.1, 512 for 7.1.</summary>
    public static int AacBitrateKbps(int channels) => channels switch
    {
        <= 1 => 96,
        2 => 192,
        <= 6 => 384,
        7 => 448,
        _ => 512,
    };

    public static string Mixdown(int channels) => channels switch
    {
        <= 1 => "mono",
        2 => "stereo",
        <= 6 => "5point1",
        7 => "6point1",
        _ => "7point1",
    };

    /// <summary>True for a lossless track the plan can copy as it is.</summary>
    public static bool IsLossless(string? codec, string? profile) => LosslessCopy(codec ?? "", profile) is not null;

    // HandBrake drops a track whose codec does not match its passthrough, so each names its own.
    private static string? LosslessCopy(string codec, string? profile)
    {
        if (IsDtsHdMa(codec, profile))
        {
            return "copy:dtshd";
        }

        return codec.ToLowerInvariant() switch
        {
            "flac" => "copy:flac",
            "truehd" => "copy:truehd",
            "alac" => "copy:alac",
            var c when c.StartsWith("pcm_", StringComparison.Ordinal) => "copy:pcm",
            _ => null,
        };
    }

    // ffprobe reports "DTS-HD MA", and newer builds "DTS-HD MA + DTS:X".
    private static bool IsDtsHdMa(string codec, string? profile) =>
        string.Equals(codec, "dts", StringComparison.OrdinalIgnoreCase)
        && profile is not null
        && profile.StartsWith("DTS-HD MA", StringComparison.OrdinalIgnoreCase);
}
