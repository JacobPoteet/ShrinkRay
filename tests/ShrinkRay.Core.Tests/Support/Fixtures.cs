using ShrinkRay.Core.Media;

namespace ShrinkRay.Core.Tests.Support;

internal static class Fixtures
{
    public static string Json(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json"));

    public static MediaInfo Probe(string name) => FfprobeParser.Parse(Json(name));
}

/// <summary>Builds MediaInfo values in code for tests that need many small variations.</summary>
internal sealed class MediaBuilder
{
    private readonly List<MediaStream> _streams = [];
    private readonly string _fileName;
    private readonly string _formatName;
    private double? _duration = 1400;
    private long? _size = 1_000_000_000;

    private MediaBuilder(string fileName, string formatName)
    {
        _fileName = fileName;
        _formatName = formatName;
    }

    public static MediaBuilder Mkv(string fileName = "Example Show S01E01.mkv") => new(fileName, "matroska,webm");

    public static MediaBuilder Mp4(string fileName = "Example Show S01E01.mp4") => new(fileName, "mov,mp4,m4a,3gp,3g2,mj2");

    public MediaBuilder Duration(double? seconds)
    {
        _duration = seconds;
        return this;
    }

    public MediaBuilder Video(string codec = "h264", bool attachedPicture = false) =>
        Add(new MediaStream { Type = StreamType.Video, CodecName = codec, Width = 1920, Height = 1080, IsAttachedPicture = attachedPicture });

    public MediaBuilder Audio(string codec, int channels = 2, string? profile = null) =>
        Add(new MediaStream { Type = StreamType.Audio, CodecName = codec, Channels = channels, Profile = profile });

    public MediaBuilder Subtitle(string codec) => Add(new MediaStream { Type = StreamType.Subtitle, CodecName = codec });

    public MediaBuilder Font(string fileName = "ExampleSans.ttf") =>
        Add(new MediaStream { Type = StreamType.Attachment, CodecName = "ttf", AttachmentFileName = fileName, AttachmentMimeType = "application/x-truetype-font" });

    public MediaInfo Build() => new()
    {
        FileName = _fileName,
        FormatName = _formatName,
        DurationSeconds = _duration,
        SizeBytes = _size,
        Streams = _streams.ToList(),
    };

    private MediaBuilder Add(MediaStream stream)
    {
        _streams.Add(stream with { Index = _streams.Count });
        return this;
    }
}
