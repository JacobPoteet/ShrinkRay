using ShrinkRay.Core.Media;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Tests.Support;
using ShrinkRay.Core.Verification;

namespace ShrinkRay.Core.Tests.Verification;

public class OutputVerifierTests
{
    private static readonly ShrinkRaySettings Defaults = new();

    private static MediaInfo MkvSource() => MediaBuilder.Mkv().Duration(1400).Video()
        .Audio("flac").Audio("ac3", 6).Subtitle("ass").Subtitle("hdmv_pgs_subtitle").Font().Font("Other.ttf").Build();

    private static MediaInfo MkvOutput() => MediaBuilder.Mkv().Duration(1400.4).Video("hevc")
        .Audio("aac").Audio("ac3", 6).Subtitle("ass").Subtitle("hdmv_pgs_subtitle").Font().Font("Other.ttf").Build();

    private static VerificationResult Verify(MediaInfo source, MediaInfo output, long sourceBytes = 1000, long outputBytes = 600,
        bool decode = true, ShrinkRaySettings? settings = null) =>
        OutputVerifier.Verify(new VerificationInput(source, output, sourceBytes, outputBytes, decode), settings ?? Defaults);

    [Fact]
    public void Matching_output_with_good_savings_passes()
    {
        var result = Verify(MkvSource(), MkvOutput());

        Assert.Equal(VerificationOutcome.Pass, result.Outcome);
        Assert.True(result.Passed);
        Assert.Empty(result.Reasons);
        Assert.Equal(0.4, result.Savings, 6);
    }

    [Fact]
    public void Duration_within_one_second_passes()
    {
        Assert.True(Verify(MkvSource(), MkvOutput() with { DurationSeconds = 1401.0 }).Passed);
        Assert.True(Verify(MkvSource(), MkvOutput() with { DurationSeconds = 1399.0 }).Passed);
    }

    [Fact]
    public void Duration_off_by_more_than_one_second_fails()
    {
        var result = Verify(MkvSource(), MkvOutput() with { DurationSeconds = 1398.5 });

        Assert.Equal(VerificationOutcome.Fail, result.Outcome);
        Assert.Contains(result.Reasons, r => r.StartsWith("Duration differs"));
    }

    [Fact]
    public void Missing_duration_fails()
    {
        var result = Verify(MkvSource(), MkvOutput() with { DurationSeconds = null });

        Assert.Contains(result.Reasons, r => r.StartsWith("Duration missing"));
    }

    [Fact]
    public void Missing_video_stream_fails()
    {
        var output = MediaBuilder.Mkv().Duration(1400).Audio("aac").Audio("ac3").Subtitle("ass").Subtitle("hdmv_pgs_subtitle").Font().Font().Build();

        var result = Verify(MkvSource(), output);

        Assert.Equal(VerificationOutcome.Fail, result.Outcome);
        Assert.Contains(result.Reasons, r => r.StartsWith("Video streams"));
    }

    [Fact]
    public void Output_that_is_not_hevc_fails()
    {
        var output = MkvOutput();
        output = output with { Streams = output.Streams.Select(s => s.Type == StreamType.Video ? s with { CodecName = "h264" } : s).ToList() };

        var result = Verify(MkvSource(), output);

        Assert.Contains("Output video codec is h264, expected hevc", result.Reasons);
    }

    [Fact]
    public void Lost_audio_track_fails()
    {
        var output = MediaBuilder.Mkv().Duration(1400).Video("hevc").Audio("aac")
            .Subtitle("ass").Subtitle("hdmv_pgs_subtitle").Font().Font().Build();

        Assert.Contains("Audio streams: expected 2, output has 1", Verify(MkvSource(), output).Reasons);
    }

    [Fact]
    public void Lost_mkv_subtitle_fails()
    {
        var output = MediaBuilder.Mkv().Duration(1400).Video("hevc").Audio("aac").Audio("ac3")
            .Subtitle("ass").Font().Font().Build();

        Assert.Contains("Subtitle streams: expected 2, output has 1", Verify(MkvSource(), output).Reasons);
    }

    [Fact]
    public void Mp4_may_drop_pgs_subtitles()
    {
        var source = MediaBuilder.Mp4().Duration(1300).Video().Audio("aac").Subtitle("mov_text").Subtitle("hdmv_pgs_subtitle").Build();
        var output = MediaBuilder.Mp4().Duration(1300).Video("hevc").Audio("aac").Subtitle("mov_text").Build();

        Assert.True(Verify(source, output).Passed);
    }

    [Fact]
    public void Mp4_may_not_drop_text_subtitles()
    {
        var source = MediaBuilder.Mp4().Duration(1300).Video().Audio("aac").Subtitle("mov_text").Subtitle("mov_text").Build();
        var output = MediaBuilder.Mp4().Duration(1300).Video("hevc").Audio("aac").Subtitle("mov_text").Build();

        Assert.Equal(VerificationOutcome.Fail, Verify(source, output).Outcome);
    }

    [Fact]
    public void Lost_mkv_attachment_fails()
    {
        var output = MediaBuilder.Mkv().Duration(1400).Video("hevc").Audio("aac").Audio("ac3")
            .Subtitle("ass").Subtitle("hdmv_pgs_subtitle").Font().Build();

        Assert.Contains("Attachments: expected 2, output has 1", Verify(MkvSource(), output).Reasons);
    }

    [Fact]
    public void Attachments_are_not_checked_when_font_restore_is_off()
    {
        var output = MediaBuilder.Mkv().Duration(1400).Video("hevc").Audio("aac").Audio("ac3")
            .Subtitle("ass").Subtitle("hdmv_pgs_subtitle").Build();

        Assert.True(Verify(MkvSource(), output, settings: new ShrinkRaySettings { RestoreFonts = false }).Passed);
    }

    [Fact]
    public void Failed_decode_fails()
    {
        var result = Verify(MkvSource(), MkvOutput(), decode: false);

        Assert.Equal(VerificationOutcome.Fail, result.Outcome);
        Assert.Contains("Full decode reported errors", result.Reasons);
    }

    [Fact]
    public void Low_savings_keeps_the_original()
    {
        var result = Verify(MkvSource(), MkvOutput(), sourceBytes: 1000, outputBytes: 900);

        Assert.Equal(VerificationOutcome.KeepOriginal, result.Outcome);
        Assert.False(result.Passed);
        Assert.Single(result.Reasons);
        Assert.StartsWith("Saves 10.0%", result.Reasons[0]);
    }

    [Fact]
    public void Savings_exactly_at_threshold_pass()
    {
        Assert.True(Verify(MkvSource(), MkvOutput(), sourceBytes: 1000, outputBytes: 850).Passed);
    }

    [Fact]
    public void Larger_output_keeps_the_original()
    {
        var result = Verify(MkvSource(), MkvOutput(), sourceBytes: 1000, outputBytes: 1200);

        Assert.Equal(VerificationOutcome.KeepOriginal, result.Outcome);
        Assert.True(result.Savings < 0);
    }

    [Fact]
    public void Threshold_comes_from_settings()
    {
        var settings = new ShrinkRaySettings { MinimumSavings = 0.5 };

        Assert.Equal(VerificationOutcome.KeepOriginal, Verify(MkvSource(), MkvOutput(), settings: settings).Outcome);
    }

    [Fact]
    public void Failure_wins_over_low_savings()
    {
        var result = Verify(MkvSource(), MkvOutput(), outputBytes: 990, decode: false);

        Assert.Equal(VerificationOutcome.Fail, result.Outcome);
        Assert.DoesNotContain(result.Reasons, r => r.StartsWith("Saves"));
    }

    [Fact]
    public void Zero_byte_output_fails()
    {
        Assert.Equal(VerificationOutcome.Fail, Verify(MkvSource(), MkvOutput(), outputBytes: 0).Outcome);
    }

    [Fact]
    public void Lists_every_failure()
    {
        var output = MediaBuilder.Mkv().Duration(10).Video("h264").Build();

        var result = Verify(MkvSource(), output, decode: false);

        Assert.True(result.Reasons.Count >= 5);
    }
}
