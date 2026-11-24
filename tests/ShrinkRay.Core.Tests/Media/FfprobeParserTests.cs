using ShrinkRay.Core.Media;
using ShrinkRay.Core.Tests.Support;

namespace ShrinkRay.Core.Tests.Media;

public class FfprobeParserTests
{
    [Fact]
    public void Parses_format_fields()
    {
        var info = Fixtures.Probe("mkv_h264_fonts");

        Assert.Equal("Example Show S01E01.mkv", info.FileName);
        Assert.Equal("matroska,webm", info.FormatName);
        Assert.Equal(1420.512, info.DurationSeconds!.Value, 3);
        Assert.Equal(1_480_000_000, info.SizeBytes);
        Assert.Equal(8_335_000, info.BitRate);
    }

    [Fact]
    public void Parses_video_stream()
    {
        var video = Assert.Single(Fixtures.Probe("mkv_h264_fonts").VideoStreams);

        Assert.Equal(0, video.Index);
        Assert.Equal("h264", video.CodecName);
        Assert.Equal("High", video.Profile);
        Assert.Equal(1920, video.Width);
        Assert.Equal(1080, video.Height);
        Assert.Equal("yuv420p", video.PixelFormat);
        Assert.Equal("jpn", video.Language);
    }

    [Fact]
    public void Parses_audio_streams_and_upper_case_language_tags()
    {
        var audio = Fixtures.Probe("mkv_h264_fonts").AudioStreams.ToList();

        Assert.Equal(2, audio.Count);
        Assert.Equal(("flac", 2, "jpn"), (audio[0].CodecName, audio[0].Channels, audio[0].Language));
        Assert.Equal(("ac3", 6, "eng"), (audio[1].CodecName, audio[1].Channels, audio[1].Language));
    }

    [Fact]
    public void Parses_subtitles_and_attachments()
    {
        var info = Fixtures.Probe("mkv_h264_fonts");

        Assert.Equal(["ass", "hdmv_pgs_subtitle"], info.SubtitleStreams.Select(s => s.CodecName));
        var fonts = info.AttachmentStreams.ToList();
        Assert.Equal(2, fonts.Count);
        Assert.Equal("ExampleSans-Regular.ttf", fonts[0].AttachmentFileName);
        Assert.Equal("application/x-truetype-font", fonts[0].AttachmentMimeType);
        Assert.Equal("application/vnd.ms-opentype", fonts[1].AttachmentMimeType);
    }

    [Fact]
    public void Cover_art_is_not_counted_as_video()
    {
        var info = Fixtures.Probe("mp4_h264_cover");

        Assert.Equal(2, info.Streams.Count(s => s.Type == StreamType.Video));
        var video = Assert.Single(info.VideoStreams);
        Assert.Equal("h264", video.CodecName);
        Assert.True(info.Streams[3].IsAttachedPicture);
    }

    [Fact]
    public void Accepts_numeric_duration()
    {
        Assert.Equal(1302.4, Fixtures.Probe("mp4_h264_cover").DurationSeconds);
    }

    [Fact]
    public void Missing_fields_become_null()
    {
        var info = FfprobeParser.Parse("""{ "streams": [ { "index": 0, "codec_type": "data" } ], "format": { } }""");

        Assert.Null(info.DurationSeconds);
        Assert.Null(info.SizeBytes);
        Assert.Equal("", info.FormatName);
        var stream = Assert.Single(info.Streams);
        Assert.Equal(StreamType.Data, stream.Type);
        Assert.Null(stream.CodecName);
        Assert.Null(stream.Channels);
    }

    [Fact]
    public void Empty_object_gives_empty_info()
    {
        var info = FfprobeParser.Parse("{}");

        Assert.Empty(info.Streams);
        Assert.Null(info.FileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void Rejects_text_that_is_not_ffprobe_json(string text)
    {
        Assert.Throws<FormatException>(() => FfprobeParser.Parse(text));
    }
}
