using ShrinkRay.Core.Media;
using ShrinkRay.Core.Tests.Support;

namespace ShrinkRay.Core.Tests.Media;

public class EligibilityTests
{
    [Fact]
    public void H264_mkv_is_eligible()
    {
        var result = Eligibility.Evaluate(Fixtures.Probe("mkv_h264_fonts"));

        Assert.True(result.IsEligible);
        Assert.Equal(ContainerKind.Mkv, result.Container);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void H264_mp4_with_cover_art_is_eligible()
    {
        var result = Eligibility.Evaluate(Fixtures.Probe("mp4_h264_cover"));

        Assert.True(result.IsEligible);
        Assert.Equal(ContainerKind.Mp4, result.Container);
    }

    [Fact]
    public void H264_m4v_is_eligible_as_m4v()
    {
        var result = Eligibility.Evaluate(MediaBuilder.Mp4("Example Show S01E01.m4v").Video().Build());

        Assert.True(result.IsEligible);
        Assert.Equal(ContainerKind.M4v, result.Container);
    }

    [Theory]
    [InlineData("hevc")]
    [InlineData("av1")]
    [InlineData("vp9")]
    public void Efficient_codecs_are_skipped(string codec)
    {
        var result = Eligibility.Evaluate(MediaBuilder.Mkv().Video(codec).Build());

        Assert.False(result.IsEligible);
        Assert.Equal(SkipReason.AlreadyEfficientCodec, result.Reason);
        Assert.Equal($"Already {codec}", result.Message);
    }

    [Fact]
    public void Hevc_fixture_is_skipped()
    {
        Assert.Equal(SkipReason.AlreadyEfficientCodec, Eligibility.Evaluate(Fixtures.Probe("mkv_hevc")).Reason);
    }

    [Fact]
    public void Avi_is_an_unsupported_container()
    {
        var result = Eligibility.Evaluate(Fixtures.Probe("avi_h264"));

        Assert.False(result.IsEligible);
        Assert.Equal(SkipReason.UnsupportedContainer, result.Reason);
        Assert.Equal("Unsupported container", result.Message);
    }

    [Theory]
    [InlineData("Example.webm", "matroska,webm")]
    [InlineData("Example.mov", "mov,mp4,m4a,3gp,3g2,mj2")]
    [InlineData("Example.mkv", "avi")]
    [InlineData("Example.mp4", "matroska,webm")]
    [InlineData("Example.ts", "mpegts")]
    public void Other_containers_are_unsupported(string fileName, string formatName)
    {
        var info = MediaBuilder.Mkv().Video().Build() with { FileName = fileName, FormatName = formatName };

        Assert.Equal(SkipReason.UnsupportedContainer, Eligibility.Evaluate(info).Reason);
    }

    [Fact]
    public void Extension_check_ignores_case()
    {
        var info = MediaBuilder.Mkv("Example Show S01E01.MKV").Video().Build();

        Assert.True(Eligibility.Evaluate(info).IsEligible);
    }

    [Fact]
    public void File_without_video_is_skipped()
    {
        var result = Eligibility.Evaluate(Fixtures.Probe("mkv_audio_only"));

        Assert.Equal(SkipReason.NoVideoStream, result.Reason);
    }

    [Fact]
    public void Cover_art_alone_does_not_count_as_video()
    {
        var info = MediaBuilder.Mp4().Video("mjpeg", attachedPicture: true).Audio("aac").Build();

        Assert.Equal(SkipReason.NoVideoStream, Eligibility.Evaluate(info).Reason);
    }

    [Fact]
    public void Other_video_codecs_are_skipped()
    {
        var result = Eligibility.Evaluate(MediaBuilder.Mkv().Video("mpeg4").Build());

        Assert.Equal(SkipReason.UnsupportedVideoCodec, result.Reason);
        Assert.Contains("mpeg4", result.Message);
    }

    [Fact]
    public void Several_video_streams_are_skipped()
    {
        var result = Eligibility.Evaluate(MediaBuilder.Mkv().Video().Video().Build());

        Assert.Equal(SkipReason.MultipleVideoStreams, result.Reason);
    }
}
