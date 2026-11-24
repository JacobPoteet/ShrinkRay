using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Tests.Support;

namespace ShrinkRay.Core.Tests.Encode;

public class FontRestorePlannerTests
{
    private static readonly MediaInfo NoFonts = MediaBuilder.Mkv().Video("hevc").Audio("aac").Build();

    private static FontRestorePlan? Plan(MediaInfo source, MediaInfo encoded, ShrinkRaySettings? settings = null) =>
        FontRestorePlanner.Plan(source, encoded, "encoded.mkv", "source.mkv", "out.mkv", settings ?? new ShrinkRaySettings());

    [Fact]
    public void Encode_without_fonts_copies_attachments_from_the_source()
    {
        var plan = Plan(Fixtures.Probe("mkv_h264_fonts"), NoFonts);

        Assert.NotNull(plan);
        Assert.Equal(2, plan.AttachmentCount);
        var args = string.Join(' ', plan.Arguments);
        Assert.Contains("-i encoded.mkv -i source.mkv -map 0 -map 1:t -c copy out.mkv", args);
        Assert.Equal("out.mkv", plan.Arguments[^1]);
    }

    [Fact]
    public void Encode_that_kept_every_font_needs_nothing()
    {
        var encoded = MediaBuilder.Mkv().Video("hevc").Audio("aac").Font("A.ttf").Font("B.otf").Build();

        Assert.Null(Plan(Fixtures.Probe("mkv_h264_fonts"), encoded));
    }

    [Fact]
    public void Encode_that_lost_some_fonts_replaces_its_attachments_with_the_sources()
    {
        var encoded = MediaBuilder.Mkv().Video("hevc").Audio("aac").Font("A.ttf").Build();

        var plan = Plan(Fixtures.Probe("mkv_h264_fonts"), encoded);

        Assert.NotNull(plan);
        Assert.Contains("-map 0 -map -0:t -map 1:t -c copy", string.Join(' ', plan.Arguments));
    }

    [Fact]
    public void Mkv_without_attachments_needs_nothing()
    {
        var source = MediaBuilder.Mkv().Video().Audio("aac").Build();

        Assert.Null(Plan(source, NoFonts));
    }

    [Fact]
    public void Mp4_needs_nothing()
    {
        var source = MediaBuilder.Mp4().Video().Font().Build();

        Assert.Null(FontRestorePlanner.Plan(source, MediaBuilder.Mp4().Video("hevc").Build(), "e.mp4", "s.mp4", "o.mp4", new ShrinkRaySettings()));
    }

    [Fact]
    public void Setting_off_skips_the_restore()
    {
        Assert.Null(Plan(Fixtures.Probe("mkv_h264_fonts"), NoFonts, new ShrinkRaySettings { RestoreFonts = false }));
    }
}
