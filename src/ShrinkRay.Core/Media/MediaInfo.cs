namespace ShrinkRay.Core.Media;

public enum StreamType
{
    Unknown,
    Video,
    Audio,
    Subtitle,
    Attachment,
    Data,
}

/// <summary>One stream from ffprobe's <c>streams</c> array.</summary>
public sealed record MediaStream
{
    public int Index { get; init; }
    public StreamType Type { get; init; }
    public string? CodecName { get; init; }
    public string? Profile { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? PixelFormat { get; init; }

    /// <summary>Average frames per second, from <c>avg_frame_rate</c> or else <c>r_frame_rate</c>.</summary>
    public double? FrameRate { get; init; }
    public int? Channels { get; init; }
    public string? Language { get; init; }

    /// <summary>The <c>filename</c> tag of an attachment stream, such as a font file.</summary>
    public string? AttachmentFileName { get; init; }

    /// <summary>The <c>mimetype</c> tag of an attachment stream.</summary>
    public string? AttachmentMimeType { get; init; }

    /// <summary>True for cover art stored as a video stream (disposition <c>attached_pic</c>).</summary>
    public bool IsAttachedPicture { get; init; }
}

/// <summary>The parsed result of <c>ffprobe -show_format -show_streams -of json</c>.</summary>
public sealed record MediaInfo
{
    /// <summary>The path ffprobe was given, from <c>format.filename</c>.</summary>
    public string? FileName { get; init; }

    /// <summary>ffprobe's <c>format_name</c>, such as <c>matroska,webm</c>.</summary>
    public string FormatName { get; init; } = "";

    public double? DurationSeconds { get; init; }
    public long? SizeBytes { get; init; }
    public long? BitRate { get; init; }
    public IReadOnlyList<MediaStream> Streams { get; init; } = Array.Empty<MediaStream>();

    /// <summary>
    /// True when the video carries embedded EIA-608 closed captions (ATSC A53 data inside the
    /// video frames). HandBrake turns them into one extra subtitle track. The parser sets it from
    /// ffprobe's stream-level <c>closed_captions</c> flag, which ffprobe 9 no longer prints;
    /// <see cref="IMediaProbe.DetectClosedCaptionsAsync"/> sets it from the frames themselves.
    /// </summary>
    public bool HasClosedCaptions { get; init; }

    /// <summary>Real video streams. Cover art pictures are left out.</summary>
    public IEnumerable<MediaStream> VideoStreams =>
        Streams.Where(s => s.Type == StreamType.Video && !s.IsAttachedPicture);

    public IEnumerable<MediaStream> AudioStreams => Streams.Where(s => s.Type == StreamType.Audio);

    public IEnumerable<MediaStream> SubtitleStreams => Streams.Where(s => s.Type == StreamType.Subtitle);

    public IEnumerable<MediaStream> AttachmentStreams => Streams.Where(s => s.Type == StreamType.Attachment);
}
