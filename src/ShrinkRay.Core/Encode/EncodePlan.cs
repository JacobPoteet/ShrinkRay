using System.Globalization;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Settings;

namespace ShrinkRay.Core.Encode;

/// <summary>Everything HandBrakeCLI needs for one file.</summary>
public sealed record EncodePlan(
    ContainerKind Container,
    string InputPath,
    string OutputPath,
    IReadOnlyList<AudioTrackPlan> Audio,
    bool AllSubtitles,
    IReadOnlyList<int> SubtitleTracks,
    IReadOnlyList<string> Arguments);

public static class EncodePlanner
{
    public const string VideoEncoder = "nvenc_h265_10bit";
    public const string EncoderPreset = "slowest";

    /// <summary>
    /// Builds the HandBrakeCLI arguments for an eligible file. The output keeps the source
    /// container and extension, every audio track, the chapters, and every subtitle the
    /// container can hold.
    /// </summary>
    /// <param name="showFolderName">The show's folder name. A matching entry in
    /// <see cref="ShrinkRaySettings.ShowCqOverrides"/> sets the CQ; otherwise the default applies.</param>
    /// <exception cref="ArgumentException">The source is not eligible, or the output extension differs.</exception>
    public static EncodePlan Build(MediaInfo source, string inputPath, string outputPath, ShrinkRaySettings settings, string? showFolderName = null)
    {
        var eligibility = Eligibility.Evaluate(source);
        if (!eligibility.IsEligible)
        {
            throw new ArgumentException($"Source is not eligible: {eligibility.Message}", nameof(source));
        }

        if (!string.Equals(Path.GetExtension(inputPath), Path.GetExtension(outputPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The output must keep the source extension.", nameof(outputPath));
        }

        var container = eligibility.Container;
        var audio = AudioPlanner.Plan(source);
        var allSubtitles = container == ContainerKind.Mkv;
        var subtitleTracks = allSubtitles ? Array.Empty<int>() : Mp4SubtitleTracks(source);

        var args = new List<string>
        {
            "-i", inputPath,
            "-o", outputPath,
            "-f", container == ContainerKind.Mkv ? "av_mkv" : "av_mp4",
            "--markers",
            "-e", VideoEncoder,
            "-q", settings.CqForShow(showFolderName).ToString(CultureInfo.InvariantCulture),
            "--encoder-preset", EncoderPreset,
            // HandBrake auto-crops by default and can misjudge dark scenes. Keep the full frame.
            "--crop", "0:0:0:0",
        };

        if (audio.Count > 0)
        {
            args.Add("--audio");
            args.Add(Join(audio.Select(a => a.TrackNumber.ToString(CultureInfo.InvariantCulture))));
            args.Add("--aencoder");
            args.Add(Join(audio.Select(a => a.Encoder)));
            args.Add("--ab");
            args.Add(Join(audio.Select(a => a.BitrateKbps.ToString(CultureInfo.InvariantCulture))));
            args.Add("--mixdown");
            args.Add(Join(audio.Select(a => a.Mixdown)));
            args.Add("--audio-fallback");
            args.Add(AudioPlanner.AacEncoder);
        }

        if (allSubtitles)
        {
            args.Add("--all-subtitles");
        }
        else if (subtitleTracks.Count > 0)
        {
            args.Add("--subtitle");
            args.Add(Join(subtitleTracks.Select(t => t.ToString(CultureInfo.InvariantCulture))));
        }

        return new EncodePlan(container, inputPath, outputPath, audio, allSubtitles, subtitleTracks, args);
    }

    /// <summary>
    /// MP4 can hold text subtitles only. Bitmap subtitles such as PGS would have to be burned
    /// in, so they are left out.
    /// </summary>
    public static bool IsMp4TextSubtitle(MediaStream stream) =>
        stream.CodecName is "mov_text" or "subrip";

    /// <summary>HandBrake's 1-based subtitle track numbers for the text subtitles in an MP4 source.</summary>
    public static IReadOnlyList<int> Mp4SubtitleTracks(MediaInfo source) =>
        source.SubtitleStreams
            .Select((stream, i) => (stream, track: i + 1))
            .Where(x => IsMp4TextSubtitle(x.stream))
            .Select(x => x.track)
            .ToList();

    private static string Join(IEnumerable<string> values) => string.Join(',', values);
}
