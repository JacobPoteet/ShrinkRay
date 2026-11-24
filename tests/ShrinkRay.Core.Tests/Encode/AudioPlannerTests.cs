using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Tests.Support;

namespace ShrinkRay.Core.Tests.Encode;

public class AudioPlannerTests
{
    [Theory]
    [InlineData("aac", null, "copy:aac")]
    [InlineData("ac3", null, "copy:ac3")]
    [InlineData("eac3", null, "copy:eac3")]
    [InlineData("mp3", null, "copy:mp3")]
    [InlineData("dts", "DTS", "copy:dts")]
    [InlineData("dts", "DTS-ES", "copy:dts")]
    [InlineData("dts", "DTS-HD HRA", "copy:dts")]
    [InlineData("dts", null, "copy:dts")]
    public void Lossy_codecs_pass_through(string codec, string? profile, string encoder)
    {
        Assert.Equal((AudioAction.Passthrough, encoder), AudioPlanner.Choose(codec, profile));
    }

    [Theory]
    [InlineData("flac", null)]
    [InlineData("pcm_s16le", null)]
    [InlineData("pcm_s24le", null)]
    [InlineData("truehd", null)]
    [InlineData("dts", "DTS-HD MA")]
    [InlineData("dts", "DTS-HD MA + DTS:X")]
    public void Lossless_codecs_become_aac(string codec, string? profile)
    {
        Assert.Equal((AudioAction.EncodeAac, "av_aac"), AudioPlanner.Choose(codec, profile));
    }

    [Theory]
    [InlineData("opus")]
    [InlineData("vorbis")]
    [InlineData("wmav2")]
    [InlineData("")]
    public void Codecs_handbrake_cannot_copy_become_aac(string codec)
    {
        Assert.Equal(AudioAction.EncodeAac, AudioPlanner.Choose(codec, null).Action);
    }

    [Theory]
    [InlineData(1, 96, "mono")]
    [InlineData(2, 192, "stereo")]
    [InlineData(3, 384, "5point1")]
    [InlineData(6, 384, "5point1")]
    [InlineData(7, 448, "6point1")]
    [InlineData(8, 512, "7point1")]
    public void Bitrate_and_mixdown_follow_channel_count(int channels, int kbps, string mixdown)
    {
        Assert.Equal(kbps, AudioPlanner.AacBitrateKbps(channels));
        Assert.Equal(mixdown, AudioPlanner.Mixdown(channels));
    }

    [Fact]
    public void Track_numbers_count_audio_streams_only()
    {
        var plans = AudioPlanner.Plan(Fixtures.Probe("mkv_h264_fonts"));

        Assert.Equal([1, 2], plans.Select(p => p.TrackNumber));
        Assert.Equal([1, 2], plans.Select(p => p.StreamIndex));
        Assert.Equal(AudioAction.EncodeAac, plans[0].Action);
        Assert.Equal(AudioAction.Passthrough, plans[1].Action);
    }

    [Fact]
    public void Missing_channel_count_is_treated_as_stereo()
    {
        var source = MediaBuilder.Mkv().Video().Build();
        source = source with
        {
            Streams = [.. source.Streams, new Core.Media.MediaStream { Index = 1, Type = Core.Media.StreamType.Audio, CodecName = "flac" }],
        };

        var plan = Assert.Single(AudioPlanner.Plan(source));

        Assert.Equal(192, plan.BitrateKbps);
        Assert.Equal("stereo", plan.Mixdown);
    }
}
