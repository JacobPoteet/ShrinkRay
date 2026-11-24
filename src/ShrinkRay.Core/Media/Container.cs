namespace ShrinkRay.Core.Media;

public enum ContainerKind
{
    Unsupported,
    Mkv,
    Mp4,
    M4v,
}

public static class Container
{
    /// <summary>
    /// Works out the container from the file extension and checks that ffprobe's format name
    /// agrees. A <c>.webm</c> file reports <c>matroska,webm</c> but is not supported, and an
    /// <c>.mkv</c> that is really something else is not either.
    /// </summary>
    public static ContainerKind Detect(MediaInfo info) => Detect(info.FileName, info.FormatName);

    public static ContainerKind Detect(string? fileName, string formatName)
    {
        var formats = formatName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var extension = string.IsNullOrEmpty(fileName) ? "" : Path.GetExtension(fileName).ToLowerInvariant();

        return extension switch
        {
            ".mkv" when formats.Contains("matroska") => ContainerKind.Mkv,
            ".mp4" when formats.Contains("mp4") => ContainerKind.Mp4,
            ".m4v" when formats.Contains("mp4") => ContainerKind.M4v,
            _ => ContainerKind.Unsupported,
        };
    }

    public static bool IsMp4Family(this ContainerKind kind) => kind is ContainerKind.Mp4 or ContainerKind.M4v;
}
