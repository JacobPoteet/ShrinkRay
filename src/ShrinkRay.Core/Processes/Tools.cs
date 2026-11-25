using System.Globalization;
using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Verification;

namespace ShrinkRay.Core.Processes;

/// <summary>Runs an encode plan. The real one runs HandBrakeCLI.</summary>
public interface IVideoEncoder
{
    Task EncodeAsync(EncodePlan plan, IProgress<EncodeProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>Copies font attachments into an encode. The real one runs ffmpeg.</summary>
public interface IFontRestorer
{
    Task RestoreAsync(FontRestorePlan plan, CancellationToken cancellationToken);
}

public sealed class FfprobeMediaProbe : IMediaProbe
{
    private readonly IProcessRunner _runner;
    private readonly Func<string> _ffprobePath;

    public FfprobeMediaProbe(IProcessRunner runner, string ffprobePath)
        : this(runner, () => ffprobePath)
    {
    }

    /// <param name="ffprobePath">Asked before each probe, so a tool found or moved later is used.</param>
    public FfprobeMediaProbe(IProcessRunner runner, Func<string> ffprobePath)
    {
        _runner = runner;
        _ffprobePath = ffprobePath;
    }

    public static IReadOnlyList<string> Arguments(string path) =>
        ["-v", "error", "-show_format", "-show_streams", "-of", "json", path];

    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(new ProcessSpec(_ffprobePath(), Arguments(path), CaptureStandardOutput: true), cancellationToken);
        if (result.ExitCode != 0)
        {
            throw ExternalToolException.Exited("ffprobe", result);
        }

        try
        {
            return FfprobeParser.Parse(result.StandardOutput);
        }
        catch (FormatException ex)
        {
            throw new ExternalToolException($"ffprobe output for {path} could not be read: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// How many video frames the caption check reads. A stream that carries captions repeats the
    /// caption data in every frame, padding included, so the first frames show it. 120 frames is
    /// four to five seconds at TV frame rates, enough to get past an opening frame or two without
    /// the data, and decodes in under a second.
    /// </summary>
    public const int CaptionProbeFrames = 120;

    public static IReadOnlyList<string> CaptionArguments(string path, int streamIndex) =>
    [
        "-v", "error",
        "-select_streams", streamIndex.ToString(CultureInfo.InvariantCulture),
        "-read_intervals", "%+#" + CaptionProbeFrames.ToString(CultureInfo.InvariantCulture),
        "-show_entries", "frame_side_data=side_data_type",
        "-of", "json",
        path,
    ];

    public async Task<MediaInfo> DetectClosedCaptionsAsync(string path, MediaInfo info, CancellationToken cancellationToken)
    {
        if (info.HasClosedCaptions || info.VideoStreams.FirstOrDefault() is not { } video)
        {
            return info;
        }

        var spec = new ProcessSpec(_ffprobePath(), CaptionArguments(path, video.Index), CaptureStandardOutput: true);
        var result = await _runner.RunAsync(spec, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw ExternalToolException.Exited("ffprobe", result);
        }

        try
        {
            return info with { HasClosedCaptions = FfprobeParser.HasClosedCaptionSideData(result.StandardOutput) };
        }
        catch (FormatException ex)
        {
            throw new ExternalToolException($"ffprobe frame data for {path} could not be read: {ex.Message}", ex);
        }
    }
}

public sealed class HandBrakeEncoder : IVideoEncoder
{
    private readonly IProcessRunner _runner;
    private readonly string _handBrakePath;

    public HandBrakeEncoder(IProcessRunner runner, string handBrakePath)
    {
        _runner = runner;
        _handBrakePath = handBrakePath;
    }

    public async Task EncodeAsync(EncodePlan plan, IProgress<EncodeProgress>? progress, CancellationToken cancellationToken)
    {
        var spec = new ProcessSpec(_handBrakePath, plan.Arguments, OnStandardOutputLine: line =>
        {
            if (progress is not null && HandBrakeProgressParser.TryParse(line, out var p))
            {
                progress.Report(p);
            }
        });

        var result = await _runner.RunAsync(spec, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw ExternalToolException.Exited("HandBrakeCLI", result);
        }
    }
}

public sealed class FfmpegFontRestorer : IFontRestorer
{
    private readonly IProcessRunner _runner;
    private readonly string _ffmpegPath;

    public FfmpegFontRestorer(IProcessRunner runner, string ffmpegPath)
    {
        _runner = runner;
        _ffmpegPath = ffmpegPath;
    }

    public async Task RestoreAsync(FontRestorePlan plan, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(new ProcessSpec(_ffmpegPath, plan.Arguments), cancellationToken);
        if (result.ExitCode != 0)
        {
            throw ExternalToolException.Exited("ffmpeg", result);
        }
    }
}

/// <summary>Decodes the whole file with ffmpeg. Clean means exit code 0 and nothing on stderr at the error level.</summary>
public sealed class FfmpegDecodeChecker : IDecodeChecker
{
    private readonly IProcessRunner _runner;
    private readonly string _ffmpegPath;

    public FfmpegDecodeChecker(IProcessRunner runner, string ffmpegPath)
    {
        _runner = runner;
        _ffmpegPath = ffmpegPath;
    }

    public static IReadOnlyList<string> Arguments(string path) =>
        ["-hide_banner", "-nostdin", "-v", "error", "-i", path, "-f", "null", "-"];

    public async Task<bool> DecodesCleanlyAsync(string path, CancellationToken cancellationToken)
    {
        var errorLines = 0;
        var spec = new ProcessSpec(_ffmpegPath, Arguments(path), OnStandardErrorLine: _ => errorLines++);
        var result = await _runner.RunAsync(spec, cancellationToken);
        return result.ExitCode == 0 && errorLines == 0;
    }
}
