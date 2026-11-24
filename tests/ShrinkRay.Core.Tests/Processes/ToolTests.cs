using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Processes;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Tests.Support;

namespace ShrinkRay.Core.Tests.Processes;

public class ToolTests
{
    [Fact]
    public async Task Ffprobe_runs_with_json_arguments_and_parses_stdout()
    {
        var runner = new FakeProcessRunner();
        runner.StandardOutputLines.AddRange(Fixtures.Json("mkv_h264_fonts").Split('\n'));
        var probe = new FfprobeMediaProbe(runner, "ffprobe.exe");

        var info = await probe.ProbeAsync(@"C:\Scratch\in file.mkv", CancellationToken.None);

        var call = Assert.Single(runner.Calls);
        Assert.Equal("ffprobe.exe", call.FileName);
        Assert.Equal(["-v", "error", "-show_format", "-show_streams", "-of", "json", @"C:\Scratch\in file.mkv"], call.Arguments);
        Assert.True(call.CaptureStandardOutput);
        Assert.Equal(7, info.Streams.Count);
    }

    [Fact]
    public async Task Ffprobe_failure_throws_with_stderr()
    {
        var runner = new FakeProcessRunner { ExitCode = 1 };
        runner.StandardErrorLines.Add("in.mkv: Invalid data found when processing input");

        var ex = await Assert.ThrowsAsync<ExternalToolException>(() =>
            new FfprobeMediaProbe(runner, "ffprobe.exe").ProbeAsync("in.mkv", CancellationToken.None));

        Assert.Contains("Invalid data", ex.Message);
    }

    [Fact]
    public async Task Ffprobe_garbage_output_throws()
    {
        var runner = new FakeProcessRunner();
        runner.StandardOutputLines.Add("not json");

        await Assert.ThrowsAsync<ExternalToolException>(() =>
            new FfprobeMediaProbe(runner, "ffprobe.exe").ProbeAsync("in.mkv", CancellationToken.None));
    }

    [Fact]
    public async Task Caption_check_reads_side_data_from_the_first_video_frames()
    {
        var runner = new FakeProcessRunner();
        runner.StandardOutputLines.AddRange(Fixtures.Json("frames_captions").Split('\n'));
        var info = MediaBuilder.Mp4().Video("mjpeg", attachedPicture: true).Video().Audio("aac").Build();

        var result = await new FfprobeMediaProbe(runner, "ffprobe.exe").DetectClosedCaptionsAsync(@"C:\Scratch\in.mp4", info, CancellationToken.None);

        Assert.True(result.HasClosedCaptions);
        Assert.Equal(info.Streams, result.Streams);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(
            ["-v", "error", "-select_streams", "1", "-read_intervals", "%+#120", "-show_entries", "frame_side_data=side_data_type", "-of", "json", @"C:\Scratch\in.mp4"],
            call.Arguments);
        Assert.True(call.CaptureStandardOutput);
    }

    [Fact]
    public async Task Caption_check_finds_none_in_plain_frames()
    {
        var runner = new FakeProcessRunner();
        runner.StandardOutputLines.AddRange(Fixtures.Json("frames_no_captions").Split('\n'));

        var result = await new FfprobeMediaProbe(runner, "ffprobe.exe")
            .DetectClosedCaptionsAsync("in.mkv", MediaBuilder.Mkv().Video().Build(), CancellationToken.None);

        Assert.False(result.HasClosedCaptions);
    }

    [Fact]
    public async Task Caption_check_is_skipped_when_the_stream_flag_already_says_so_or_there_is_no_video()
    {
        var runner = new FakeProcessRunner();
        var probe = new FfprobeMediaProbe(runner, "ffprobe.exe");

        Assert.True((await probe.DetectClosedCaptionsAsync("a.mkv", Fixtures.Probe("mkv_h264_cc"), CancellationToken.None)).HasClosedCaptions);
        Assert.False((await probe.DetectClosedCaptionsAsync("b.mkv", MediaBuilder.Mkv().Audio("aac").Build(), CancellationToken.None)).HasClosedCaptions);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Caption_check_failure_throws()
    {
        var runner = new FakeProcessRunner { ExitCode = 1 };

        await Assert.ThrowsAsync<ExternalToolException>(() =>
            new FfprobeMediaProbe(runner, "ffprobe.exe").DetectClosedCaptionsAsync("in.mkv", MediaBuilder.Mkv().Video().Build(), CancellationToken.None));
    }

    [Fact]
    public async Task HandBrake_reports_progress_from_stdout()
    {
        var runner = new FakeProcessRunner();
        runner.StandardOutputLines.AddRange(
        [
            "Encoding: task 1 of 1, 10.00 %",
            "Encoding: task 1 of 1, 41.23 % (312.45 fps, avg 298.10 fps, ETA 00h01m10s)",
            "Muxing: this may take awhile...",
        ]);
        var plan = EncodePlanner.Build(Fixtures.Probe("mkv_h264_fonts"), "in.mkv", "out.mkv", new ShrinkRaySettings());
        var reports = new List<EncodeProgress>();

        await new HandBrakeEncoder(runner, "HandBrakeCLI.exe").EncodeAsync(plan, new SyncProgress<EncodeProgress>(reports.Add), CancellationToken.None);

        Assert.Equal(plan.Arguments, Assert.Single(runner.Calls).Arguments);
        Assert.Equal([10.0, 41.23], reports.Select(r => r.Percent));
    }

    [Fact]
    public async Task HandBrake_failure_throws()
    {
        var runner = new FakeProcessRunner { ExitCode = 3 };
        var plan = EncodePlanner.Build(Fixtures.Probe("mkv_h264_fonts"), "in.mkv", "out.mkv", new ShrinkRaySettings());

        var ex = await Assert.ThrowsAsync<ExternalToolException>(() =>
            new HandBrakeEncoder(runner, "HandBrakeCLI.exe").EncodeAsync(plan, null, CancellationToken.None));

        Assert.Contains("code 3", ex.Message);
    }

    [Fact]
    public async Task Font_restorer_runs_the_plan()
    {
        var runner = new FakeProcessRunner();
        var encoded = MediaBuilder.Mkv().Video("hevc").Build();
        var plan = FontRestorePlanner.Plan(Fixtures.Probe("mkv_h264_fonts"), encoded, "e.mkv", "s.mkv", "o.mkv", new ShrinkRaySettings())!;

        await new FfmpegFontRestorer(runner, "ffmpeg.exe").RestoreAsync(plan, CancellationToken.None);

        var call = Assert.Single(runner.Calls);
        Assert.Equal("ffmpeg.exe", call.FileName);
        Assert.Equal(plan.Arguments, call.Arguments);
    }

    [Fact]
    public async Task Decode_check_passes_on_clean_exit()
    {
        var runner = new FakeProcessRunner();

        Assert.True(await new FfmpegDecodeChecker(runner, "ffmpeg.exe").DecodesCleanlyAsync("o.mkv", CancellationToken.None));
        Assert.Equal(["-hide_banner", "-nostdin", "-v", "error", "-i", "o.mkv", "-f", "null", "-"], runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task Decode_check_fails_on_any_error_line()
    {
        var runner = new FakeProcessRunner();
        runner.StandardErrorLines.Add("[hevc @ 0000] Could not find ref with POC 12");

        Assert.False(await new FfmpegDecodeChecker(runner, "ffmpeg.exe").DecodesCleanlyAsync("o.mkv", CancellationToken.None));
    }

    [Fact]
    public async Task Decode_check_fails_on_non_zero_exit()
    {
        var runner = new FakeProcessRunner { ExitCode = 1 };

        Assert.False(await new FfmpegDecodeChecker(runner, "ffmpeg.exe").DecodesCleanlyAsync("o.mkv", CancellationToken.None));
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
