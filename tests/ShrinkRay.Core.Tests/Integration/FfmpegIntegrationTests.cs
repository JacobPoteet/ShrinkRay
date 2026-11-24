using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Processes;
using ShrinkRay.Core.Settings;

namespace ShrinkRay.Core.Tests.Integration;

/// <summary>Runs only when SHRINKRAY_INTEGRATION=1. Needs ffmpeg and ffprobe on PATH.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SHRINKRAY_INTEGRATION") != "1")
        {
            Skip = "Set SHRINKRAY_INTEGRATION=1 to run tests that call ffmpeg and ffprobe.";
        }
    }
}

/// <summary>
/// Generates a two-second clip with ffmpeg's test source in a temp folder and runs the real
/// probe, font restore and decode check against it. Never touches the library.
/// </summary>
public sealed class FfmpegIntegrationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ShrinkRayIntegration", Guid.NewGuid().ToString("N"));
    private readonly ProcessRunner _runner = new();

    public FfmpegIntegrationTests()
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

    [IntegrationFact]
    public async Task Probe_font_restore_and_decode_check_work_on_a_generated_clip()
    {
        var font = Path.Combine(_folder, "ExampleFont.ttf");
        File.WriteAllBytes(font, new byte[256]);
        var source = Path.Combine(_folder, "Example Show S01E01.mkv");
        await Ffmpeg(
            "-f", "lavfi", "-i", "testsrc=duration=2:size=320x240:rate=24",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest",
            "-attach", font,
            "-metadata:s:t", "mimetype=application/x-truetype-font",
            "-metadata:s:t", "filename=ExampleFont.ttf",
            source);

        var probe = new FfprobeMediaProbe(_runner, "ffprobe.exe");
        var info = await probe.ProbeAsync(source, CancellationToken.None);

        Assert.True(Eligibility.Evaluate(info).IsEligible);
        Assert.Equal("h264", Assert.Single(info.VideoStreams).CodecName);
        var attachment = Assert.Single(info.AttachmentStreams);
        Assert.Equal("ExampleFont.ttf", attachment.AttachmentFileName);
        Assert.InRange(info.DurationSeconds!.Value, 1.5, 2.5);

        // Stand-in for HandBrake's output: the same streams without the attachment.
        var encoded = Path.Combine(_folder, "encoded.mkv");
        await Ffmpeg("-i", source, "-map", "0:v", "-map", "0:a", "-c", "copy", encoded);
        var encodedInfo = await probe.ProbeAsync(encoded, CancellationToken.None);
        Assert.Empty(encodedInfo.AttachmentStreams);

        var final = Path.Combine(_folder, "final.mkv");
        var plan = FontRestorePlanner.Plan(info, encodedInfo, encoded, source, final, new ShrinkRaySettings())!;
        await new FfmpegFontRestorer(_runner, "ffmpeg.exe").RestoreAsync(plan, CancellationToken.None);

        var restored = await probe.ProbeAsync(final, CancellationToken.None);
        Assert.Equal("ExampleFont.ttf", Assert.Single(restored.AttachmentStreams).AttachmentFileName);
        Assert.Single(restored.VideoStreams);
        Assert.Single(restored.AudioStreams);

        Assert.True(await new FfmpegDecodeChecker(_runner, "ffmpeg.exe").DecodesCleanlyAsync(final, CancellationToken.None));
    }

    [IntegrationFact]
    public async Task Partial_font_loss_is_replaced_by_the_source_attachments()
    {
        var fontA = Path.Combine(_folder, "A.ttf");
        var fontB = Path.Combine(_folder, "B.ttf");
        File.WriteAllBytes(fontA, new byte[64]);
        File.WriteAllBytes(fontB, new byte[64]);
        var source = Path.Combine(_folder, "source.mkv");
        await Ffmpeg(
            "-f", "lavfi", "-i", "testsrc=duration=1:size=160x120:rate=24",
            "-c:v", "libx264", "-pix_fmt", "yuv420p",
            "-attach", fontA, "-attach", fontB,
            "-metadata:s:t", "mimetype=application/x-truetype-font",
            source);

        // Stand-in for an encode that kept only the first font.
        var encoded = Path.Combine(_folder, "encoded.mkv");
        await Ffmpeg("-i", source, "-map", "0:v", "-map", "0:t:0", "-c", "copy", encoded);

        var probe = new FfprobeMediaProbe(_runner, "ffprobe.exe");
        var sourceInfo = await probe.ProbeAsync(source, CancellationToken.None);
        var encodedInfo = await probe.ProbeAsync(encoded, CancellationToken.None);
        Assert.Equal(2, sourceInfo.AttachmentStreams.Count());
        Assert.Single(encodedInfo.AttachmentStreams);

        var final = Path.Combine(_folder, "final.mkv");
        var plan = FontRestorePlanner.Plan(sourceInfo, encodedInfo, encoded, source, final, new ShrinkRaySettings())!;
        await new FfmpegFontRestorer(_runner, "ffmpeg.exe").RestoreAsync(plan, CancellationToken.None);

        Assert.Equal(2, (await probe.ProbeAsync(final, CancellationToken.None)).AttachmentStreams.Count());
    }

    [IntegrationFact]
    public async Task Decode_check_fails_on_a_damaged_file()
    {
        var source = Path.Combine(_folder, "clip.mkv");
        await Ffmpeg("-f", "lavfi", "-i", "testsrc=duration=2:size=320x240:rate=24", "-c:v", "libx264", "-pix_fmt", "yuv420p", source);
        var bytes = File.ReadAllBytes(source);
        var damaged = Path.Combine(_folder, "damaged.mkv");
        File.WriteAllBytes(damaged, bytes[..(bytes.Length / 2)]);

        Assert.False(await new FfmpegDecodeChecker(_runner, "ffmpeg.exe").DecodesCleanlyAsync(damaged, CancellationToken.None));
    }

    private async Task Ffmpeg(params string[] args)
    {
        var result = await _runner.RunAsync(new ProcessSpec("ffmpeg.exe", ["-hide_banner", "-nostdin", "-v", "error", "-y", .. args]), CancellationToken.None);
        Assert.True(result.ExitCode == 0, string.Join("\n", result.StandardErrorTail));
    }
}
