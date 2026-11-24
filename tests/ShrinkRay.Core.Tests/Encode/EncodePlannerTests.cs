using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Tests.Support;

namespace ShrinkRay.Core.Tests.Encode;

public class EncodePlannerTests
{
    private static readonly ShrinkRaySettings Defaults = new();

    private static string ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var i = args.ToList().IndexOf(flag);
        Assert.True(i >= 0 && i + 1 < args.Count, $"{flag} missing");
        return args[i + 1];
    }

    [Fact]
    public void Mkv_source_gives_mkv_output_with_nvenc_settings()
    {
        var plan = EncodePlanner.Build(Fixtures.Probe("mkv_h264_fonts"), "in.mkv", "out.mkv", Defaults);
        var args = plan.Arguments;

        Assert.Equal(ContainerKind.Mkv, plan.Container);
        Assert.Equal("in.mkv", ValueAfter(args, "-i"));
        Assert.Equal("out.mkv", ValueAfter(args, "-o"));
        Assert.Equal("av_mkv", ValueAfter(args, "-f"));
        Assert.Equal("nvenc_h265_10bit", ValueAfter(args, "-e"));
        Assert.Equal("24", ValueAfter(args, "-q"));
        Assert.Equal("slowest", ValueAfter(args, "--encoder-preset"));
        Assert.Contains("--markers", args);
        Assert.Contains("--all-subtitles", args);
        Assert.DoesNotContain("--subtitle", args);
    }

    [Fact]
    public void Frame_rate_is_left_to_the_source()
    {
        var args = EncodePlanner.Build(Fixtures.Probe("mkv_h264_fonts"), "in.mkv", "out.mkv", Defaults).Arguments;

        Assert.DoesNotContain("-r", args);
        Assert.DoesNotContain("--rate", args);
        Assert.DoesNotContain("--cfr", args);
    }

    [Fact]
    public void Cq_comes_from_settings()
    {
        var settings = new ShrinkRaySettings { Cq = 27 };

        var args = EncodePlanner.Build(Fixtures.Probe("mkv_h264_fonts"), "in.mkv", "out.mkv", settings).Arguments;

        Assert.Equal("27", ValueAfter(args, "-q"));
    }

    [Theory]
    [InlineData("Example.mp4", "in.mp4", "out.mp4", ContainerKind.Mp4)]
    [InlineData("Example.m4v", "in.m4v", "out.m4v", ContainerKind.M4v)]
    public void Mp4_family_gives_av_mp4_and_keeps_the_extension(string fileName, string input, string output, ContainerKind kind)
    {
        var source = MediaBuilder.Mp4(fileName).Video().Audio("aac").Build();

        var plan = EncodePlanner.Build(source, input, output, Defaults);

        Assert.Equal(kind, plan.Container);
        Assert.Equal("av_mp4", ValueAfter(plan.Arguments, "-f"));
        Assert.Equal(output, ValueAfter(plan.Arguments, "-o"));
    }

    [Fact]
    public void Output_must_keep_the_source_extension()
    {
        var source = MediaBuilder.Mp4("Example.m4v").Video().Build();

        Assert.Throws<ArgumentException>(() => EncodePlanner.Build(source, "in.m4v", "out.mp4", Defaults));
    }

    [Fact]
    public void Ineligible_source_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => EncodePlanner.Build(Fixtures.Probe("mkv_hevc"), "in.mkv", "out.mkv", Defaults));
    }

    [Fact]
    public void Every_audio_track_gets_its_own_entry()
    {
        var source = MediaBuilder.Mkv().Video()
            .Audio("flac", 2)
            .Audio("ac3", 6)
            .Audio("truehd", 8)
            .Audio("aac", 1)
            .Build();

        var args = EncodePlanner.Build(source, "in.mkv", "out.mkv", Defaults).Arguments;

        Assert.Equal("1,2,3,4", ValueAfter(args, "--audio"));
        Assert.Equal("av_aac,copy:ac3,av_aac,copy:aac", ValueAfter(args, "--aencoder"));
        Assert.Equal("192,384,512,96", ValueAfter(args, "--ab"));
        Assert.Equal("stereo,5point1,7point1,mono", ValueAfter(args, "--mixdown"));
        Assert.Equal("av_aac", ValueAfter(args, "--audio-fallback"));
        Assert.DoesNotContain("--audio-copy-mask", args);
    }

    [Fact]
    public void No_audio_means_no_audio_options()
    {
        var args = EncodePlanner.Build(MediaBuilder.Mkv().Video().Build(), "in.mkv", "out.mkv", Defaults).Arguments;

        Assert.DoesNotContain("--audio", args);
        Assert.DoesNotContain("--aencoder", args);
    }

    [Fact]
    public void Mp4_includes_only_text_subtitles()
    {
        var source = MediaBuilder.Mp4().Video()
            .Subtitle("hdmv_pgs_subtitle")
            .Subtitle("mov_text")
            .Subtitle("dvd_subtitle")
            .Subtitle("subrip")
            .Build();

        var plan = EncodePlanner.Build(source, "in.mp4", "out.mp4", Defaults);

        Assert.False(plan.AllSubtitles);
        Assert.Equal([2, 4], plan.SubtitleTracks);
        Assert.Equal("2,4", ValueAfter(plan.Arguments, "--subtitle"));
        Assert.DoesNotContain("--all-subtitles", plan.Arguments);
    }

    [Fact]
    public void Mp4_with_only_pgs_has_no_subtitle_options()
    {
        var source = MediaBuilder.Mp4().Video().Subtitle("hdmv_pgs_subtitle").Build();

        var args = EncodePlanner.Build(source, "in.mp4", "out.mp4", Defaults).Arguments;

        Assert.DoesNotContain("--subtitle", args);
        Assert.DoesNotContain("--all-subtitles", args);
    }

    [Fact]
    public void Mkv_keeps_pgs_through_all_subtitles()
    {
        var source = MediaBuilder.Mkv().Video().Subtitle("hdmv_pgs_subtitle").Build();

        Assert.Contains("--all-subtitles", EncodePlanner.Build(source, "in.mkv", "out.mkv", Defaults).Arguments);
    }

    [Fact]
    public void Paths_with_spaces_stay_single_arguments()
    {
        var args = EncodePlanner.Build(Fixtures.Probe("mkv_h264_fonts"), @"C:\scratch dir\in file.mkv", @"C:\scratch dir\out file.mkv", Defaults).Arguments;

        Assert.Equal(@"C:\scratch dir\in file.mkv", ValueAfter(args, "-i"));
    }
}
