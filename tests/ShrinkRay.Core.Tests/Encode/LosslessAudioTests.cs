using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Tests.Support;
using ShrinkRay.Core.Verification;

namespace ShrinkRay.Core.Tests.Encode;

/// <summary>The option that copies lossless audio instead of turning it into AAC.</summary>
public class LosslessAudioTests
{
    [Theory]
    [InlineData("flac", null, "copy:flac")]
    [InlineData("truehd", null, "copy:truehd")]
    [InlineData("alac", null, "copy:alac")]
    [InlineData("pcm_s24le", null, "copy:pcm")]
    [InlineData("dts", "DTS-HD MA", "copy:dtshd")]
    [InlineData("dts", "DTS-HD MA + DTS:X", "copy:dtshd")]
    public void Lossless_tracks_are_copied_when_kept_and_become_AAC_otherwise(string codec, string? profile, string copy)
    {
        Assert.Equal((AudioAction.Passthrough, copy), AudioPlanner.Choose(codec, profile, keepLossless: true));
        Assert.Equal((AudioAction.EncodeAac, AudioPlanner.AacEncoder), AudioPlanner.Choose(codec, profile));
    }

    [Theory]
    [InlineData("ac3", null, "copy:ac3")]
    [InlineData("dts", "DTS", "copy:dts")]
    [InlineData("dts", "DTS-HD HRA", "copy:dts")]
    [InlineData("opus", null, "av_aac")]
    public void Lossy_tracks_are_planned_the_same_either_way(string codec, string? profile, string encoder)
    {
        Assert.False(AudioPlanner.IsLossless(codec, profile));
        Assert.Equal(AudioPlanner.Choose(codec, profile), AudioPlanner.Choose(codec, profile, keepLossless: true));
        Assert.Equal(encoder, AudioPlanner.Choose(codec, profile, keepLossless: true).Encoder);
    }

    private static MediaInfo File(string video, string audio) =>
        MediaBuilder.Mkv().Duration(1400).Video(video).Audio(audio).Build();

    [Fact]
    public void The_setting_reaches_the_encoder_arguments()
    {
        var settings = new ShrinkRaySettings { KeepLosslessAudio = true };

        var plan = EncodePlanner.Build(File("h264", "flac"), "in.mkv", "out.mkv", settings);

        var args = plan.Arguments.ToList();
        Assert.Equal("copy:flac", args[args.IndexOf("--aencoder") + 1]);
    }

    [Theory]
    [InlineData(true, "aac", VerificationOutcome.Fail)]
    [InlineData(true, "flac", VerificationOutcome.Pass)]
    [InlineData(false, "aac", VerificationOutcome.Pass)]
    public void A_kept_track_that_came_out_lossy_fails_the_check(bool keep, string outputCodec, VerificationOutcome expected)
    {
        var settings = new ShrinkRaySettings { KeepLosslessAudio = keep };

        var result = OutputVerifier.Verify(
            new VerificationInput(File("h264", "flac"), File("hevc", outputCodec), 1000, 400, true), settings);

        Assert.Equal(expected, result.Outcome);
        if (expected == VerificationOutcome.Fail)
        {
            Assert.Equal(["Audio track 1: lossless flac came out as aac"], result.Reasons);
        }
    }

    [Fact]
    public void The_setting_saves_and_is_off_by_default()
    {
        Assert.False(SettingsStore.FromJson("{}").KeepLosslessAudio);

        var saved = SettingsStore.FromJson(SettingsStore.ToJson(new ShrinkRaySettings { KeepLosslessAudio = true }));

        Assert.True(saved.KeepLosslessAudio);
    }
}
