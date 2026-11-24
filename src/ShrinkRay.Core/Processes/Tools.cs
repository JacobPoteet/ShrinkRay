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
    private readonly string _ffprobePath;

    public FfprobeMediaProbe(IProcessRunner runner, string ffprobePath)
    {
        _runner = runner;
        _ffprobePath = ffprobePath;
    }

    public static IReadOnlyList<string> Arguments(string path) =>
        ["-v", "error", "-show_format", "-show_streams", "-of", "json", path];

    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(new ProcessSpec(_ffprobePath, Arguments(path), CaptureStandardOutput: true), cancellationToken);
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
