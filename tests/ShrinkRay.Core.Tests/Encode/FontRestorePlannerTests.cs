using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Tests.Support;

namespace ShrinkRay.Core.Tests.Encode;

public class FontRestorePlannerTests
{
    [Fact]
    public void Mkv_with_fonts_copies_attachments_from_the_source()
    {
        var plan = FontRestorePlanner.Plan(Fixtures.Probe("mkv_h264_fonts"), "encoded.mkv", "source.mkv", "out.mkv", new ShrinkRaySettings());

        Assert.NotNull(plan);
        Assert.Equal(2, plan.AttachmentCount);
        var args = string.Join(' ', plan.Arguments);
        Assert.Contains("-i encoded.mkv -i source.mkv -map 0 -map 1:t -c copy out.mkv", args);
        Assert.Equal("out.mkv", plan.Arguments[^1]);
    }

    [Fact]
    public void Mkv_without_attachments_needs_nothing()
    {
        var source = MediaBuilder.Mkv().Video().Audio("aac").Build();

        Assert.Null(FontRestorePlanner.Plan(source, "e.mkv", "s.mkv", "o.mkv", new ShrinkRaySettings()));
    }

    [Fact]
    public void Mp4_needs_nothing()
    {
        var source = MediaBuilder.Mp4().Video().Font().Build();

        Assert.Null(FontRestorePlanner.Plan(source, "e.mp4", "s.mp4", "o.mp4", new ShrinkRaySettings()));
    }

    [Fact]
    public void Setting_off_skips_the_restore()
    {
        var settings = new ShrinkRaySettings { RestoreFonts = false };

        Assert.Null(FontRestorePlanner.Plan(Fixtures.Probe("mkv_h264_fonts"), "e.mkv", "s.mkv", "o.mkv", settings));
    }
}
