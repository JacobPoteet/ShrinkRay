using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Processes;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Verification;

namespace ShrinkRay.Core.Tests.Integration;

/// <summary>
/// Runs only when SHRINKRAY_INTEGRATION=1 and HandBrakeCLI is found on PATH or in the winget
/// folders. Needs ffmpeg and ffprobe on PATH and an NVIDIA card with NVENC.
/// </summary>
public sealed class HandBrakeFactAttribute : FactAttribute
{
    public HandBrakeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SHRINKRAY_INTEGRATION") != "1")
        {
            Skip = "Set SHRINKRAY_INTEGRATION=1 to run tests that call HandBrakeCLI.";
        }
        else if (ToolLocator.ResolveOnThisMachine(ShrinkRaySettings.DefaultHandBrakeCliPath) is null)
        {
            Skip = "HandBrakeCLI was not found on PATH or in the winget folders.";
        }
    }
}

/// <summary>
/// Also needs SHRINKRAY_CC_CLIP: the path of a short H.264 clip whose video carries closed
/// captions. The clip is read in place and never copied.
/// </summary>
public sealed class CaptionClipFactAttribute : FactAttribute
{
    public const string Variable = "SHRINKRAY_CC_CLIP";

    public CaptionClipFactAttribute()
    {
        var clip = Environment.GetEnvironmentVariable(Variable);
        if (Environment.GetEnvironmentVariable("SHRINKRAY_INTEGRATION") != "1")
        {
            Skip = "Set SHRINKRAY_INTEGRATION=1 to run tests that call HandBrakeCLI.";
        }
        else if (ToolLocator.ResolveOnThisMachine(ShrinkRaySettings.DefaultHandBrakeCliPath) is null)
        {
            Skip = "HandBrakeCLI was not found on PATH or in the winget folders.";
        }
        else if (string.IsNullOrWhiteSpace(clip) || !File.Exists(clip))
        {
            Skip = $"Set {Variable} to a captioned clip to run this test.";
        }
    }
}

/// <summary>
/// Encodes with the real HandBrakeCLI and EncodePlanner's arguments in a temp folder, then runs
/// the real probe, decode check and OutputVerifier on the result. Never touches the library.
/// </summary>
public sealed class HandBrakeIntegrationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ShrinkRayIntegration", Guid.NewGuid().ToString("N"));
    private readonly ProcessRunner _runner = new();
    private readonly ShrinkRaySettings _settings = new()
    {
        HandBrakeCliPath = ToolLocator.ResolveOnThisMachine(ShrinkRaySettings.DefaultHandBrakeCliPath) ?? ShrinkRaySettings.DefaultHandBrakeCliPath,
        FfprobePath = ToolLocator.ResolveOnThisMachine(ShrinkRaySettings.DefaultFfprobePath) ?? ShrinkRaySettings.DefaultFfprobePath,
        FfmpegPath = ToolLocator.ResolveOnThisMachine(ShrinkRaySettings.DefaultFfmpegPath) ?? ShrinkRaySettings.DefaultFfmpegPath,
    };

    public HandBrakeIntegrationTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [HandBrakeFact]
    public async Task Encodes_a_generated_clip_that_passes_verification()
    {
        var subtitles = Path.Combine(_folder, "subs.srt");
        await File.WriteAllTextAsync(subtitles, "1\n00:00:00,500 --> 00:00:02,500\nExample line\n\n2\n00:00:03,000 --> 00:00:04,500\nAnother line\n");
        var source = Path.Combine(_folder, "Example Show S01E01.mkv");
        await Ffmpeg(
            "-f", "lavfi", "-i", "testsrc=duration=5:size=640x360:rate=24",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=5",
            "-i", subtitles,
            "-map", "0:v", "-map", "1:a", "-map", "2:s",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-c:s", "srt",
            source);

        var (sourceInfo, outputInfo, result) = await EncodeAndVerifyAsync(source);

        Assert.False(sourceInfo.HasClosedCaptions);
        Assert.Equal("hevc", Assert.Single(outputInfo.VideoStreams).CodecName);
        Assert.Single(outputInfo.AudioStreams);
        Assert.Single(outputInfo.SubtitleStreams);
        Assert.True(result.Outcome == VerificationOutcome.Pass, string.Join("; ", result.Reasons));
    }

    [CaptionClipFact]
    public async Task Encodes_a_captioned_clip_whose_caption_track_passes_verification()
    {
        var clip = Environment.GetEnvironmentVariable(CaptionClipFactAttribute.Variable)!;

        var (sourceInfo, outputInfo, result) = await EncodeAndVerifyAsync(clip);

        Assert.True(sourceInfo.HasClosedCaptions);
        Assert.Equal(sourceInfo.SubtitleStreams.Count() + 1, outputInfo.SubtitleStreams.Count());
        Assert.True(result.Outcome == VerificationOutcome.Pass, string.Join("; ", result.Reasons));
    }

    private async Task<(MediaInfo Source, MediaInfo Output, VerificationResult Result)> EncodeAndVerifyAsync(string source)
    {
        var probe = new FfprobeMediaProbe(_runner, _settings.FfprobePath);
        var sourceInfo = await probe.ProbeAsync(source, CancellationToken.None);
        sourceInfo = await probe.DetectClosedCaptionsAsync(source, sourceInfo, CancellationToken.None);
        Assert.True(Eligibility.Evaluate(sourceInfo).IsEligible);

        var output = Path.Combine(_folder, "encoded" + Path.GetExtension(source));
        var plan = EncodePlanner.Build(sourceInfo, source, output, _settings);
        await new HandBrakeEncoder(_runner, _settings.HandBrakeCliPath).EncodeAsync(plan, null, CancellationToken.None);

        var outputInfo = await probe.ProbeAsync(output, CancellationToken.None);
        var decoded = await new FfmpegDecodeChecker(_runner, _settings.FfmpegPath).DecodesCleanlyAsync(output, CancellationToken.None);

        // These tests check that the encode is sound. Savings on a short clip say nothing about a real episode.
        var verifySettings = _settings.Clone();
        verifySettings.MinimumSavings = -100;
        var result = OutputVerifier.Verify(
            new VerificationInput(sourceInfo, outputInfo, new FileInfo(source).Length, new FileInfo(output).Length, decoded),
            verifySettings);
        return (sourceInfo, outputInfo, result);
    }

    private async Task Ffmpeg(params string[] args)
    {
        var result = await _runner.RunAsync(
            new ProcessSpec(_settings.FfmpegPath, ["-hide_banner", "-nostdin", "-v", "error", "-y", .. args]), CancellationToken.None);
        Assert.True(result.ExitCode == 0, string.Join("\n", result.StandardErrorTail));
    }
}
