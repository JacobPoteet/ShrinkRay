using System.Globalization;
using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Settings;

namespace ShrinkRay.Core.Verification;

public enum VerificationOutcome
{
    /// <summary>Every check passed and the savings meet the threshold. The original may be replaced.</summary>
    Pass,

    /// <summary>The encode is sound but saves too little. Keep the original.</summary>
    KeepOriginal,

    /// <summary>At least one check failed. Keep the original.</summary>
    Fail,
}

public sealed record VerificationResult(VerificationOutcome Outcome, IReadOnlyList<string> Reasons, double Savings)
{
    public bool Passed => Outcome == VerificationOutcome.Pass;
}

public sealed record VerificationInput(
    MediaInfo Source,
    MediaInfo Output,
    long SourceBytes,
    long OutputBytes,
    bool DecodePassed);

/// <summary>Decides whether an encoded file may replace its source.</summary>
public static class OutputVerifier
{
    public const double MaxDurationDifferenceSeconds = 1.0;

    public static VerificationResult Verify(VerificationInput input, ShrinkRaySettings settings)
    {
        var failures = new List<string>();
        var source = input.Source;
        var output = input.Output;
        var container = Container.Detect(source);

        if (source.DurationSeconds is not { } sourceDuration || output.DurationSeconds is not { } outputDuration)
        {
            failures.Add("Duration missing from the source or the output");
        }
        else if (Math.Abs(sourceDuration - outputDuration) > MaxDurationDifferenceSeconds)
        {
            failures.Add($"Duration differs by {Format(Math.Abs(sourceDuration - outputDuration))} s " +
                $"(source {Format(sourceDuration)} s, output {Format(outputDuration)} s)");
        }

        var sourceVideo = source.VideoStreams.Count();
        var outputVideo = output.VideoStreams.ToList();
        if (sourceVideo != 1 || outputVideo.Count != 1)
        {
            failures.Add($"Video streams: source {sourceVideo}, output {outputVideo.Count}, expected 1 each");
        }

        if (outputVideo.Count > 0 && !string.Equals(outputVideo[0].CodecName, "hevc", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"Output video codec is {outputVideo[0].CodecName ?? "unknown"}, expected hevc");
        }

        CompareCount(failures, "Audio streams", source.AudioStreams.Count(), output.AudioStreams.Count());

        // HandBrake can fall back to AAC without failing, which would lose what the user asked to keep.
        if (settings.KeepLosslessAudio)
        {
            CompareLossless(failures, source.AudioStreams.ToList(), output.AudioStreams.ToList());
        }

        // MP4 output keeps text subtitles only, so bitmap tracks dropped by the plan are expected.
        // Captions carried in the video come out as exactly one extra track in either container:
        // --all-subtitles takes them in MKV and the plan names them in MP4.
        var expectedSubtitles = container.IsMp4Family()
            ? source.SubtitleStreams.Count(EncodePlanner.IsMp4TextSubtitle)
            : source.SubtitleStreams.Count();
        if (source.HasClosedCaptions)
        {
            expectedSubtitles++;
        }

        CompareCount(failures, source.HasClosedCaptions ? "Subtitle streams (closed captions add one)" : "Subtitle streams",
            expectedSubtitles, output.SubtitleStreams.Count());

        // With font restore turned off the user has accepted losing attachments.
        if (container == ContainerKind.Mkv && settings.RestoreFonts)
        {
            CompareCount(failures, "Attachments", source.AttachmentStreams.Count(), output.AttachmentStreams.Count());
        }

        if (!input.DecodePassed)
        {
            failures.Add("Full decode reported errors");
        }

        if (input.SourceBytes <= 0 || input.OutputBytes <= 0)
        {
            failures.Add($"File size missing (source {input.SourceBytes} bytes, output {input.OutputBytes} bytes)");
        }

        var savings = input.SourceBytes > 0 ? 1.0 - (double)input.OutputBytes / input.SourceBytes : 0;
        if (failures.Count > 0)
        {
            return new VerificationResult(VerificationOutcome.Fail, failures, savings);
        }

        if (savings < settings.MinimumSavings)
        {
            return new VerificationResult(
                VerificationOutcome.KeepOriginal,
                [$"Saves {Percent(savings)}, below the {Percent(settings.MinimumSavings)} minimum"],
                savings);
        }

        return new VerificationResult(VerificationOutcome.Pass, [], savings);
    }

    private static void CompareLossless(List<string> failures, List<MediaStream> source, List<MediaStream> output)
    {
        for (var i = 0; i < Math.Min(source.Count, output.Count); i++)
        {
            var (was, now) = (source[i], output[i]);
            if (AudioPlanner.IsLossless(was.CodecName, was.Profile)
                && (!string.Equals(was.CodecName, now.CodecName, StringComparison.OrdinalIgnoreCase)
                    || !AudioPlanner.IsLossless(now.CodecName, now.Profile)))
            {
                failures.Add($"Audio track {i + 1}: lossless {was.CodecName} came out as {now.CodecName ?? "unknown"}");
            }
        }
    }

    private static void CompareCount(List<string> failures, string what, int expected, int actual)
    {
        if (expected != actual)
        {
            failures.Add($"{what}: expected {expected}, output has {actual}");
        }
    }

    private static string Format(double seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Percent(double fraction) => (fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
