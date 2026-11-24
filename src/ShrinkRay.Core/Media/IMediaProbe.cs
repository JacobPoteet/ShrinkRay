namespace ShrinkRay.Core.Media;

/// <summary>Reads a file's container and streams. The real one runs ffprobe.</summary>
public interface IMediaProbe
{
    /// <summary>Reads the container and stream headers. Fast enough for a library scan.</summary>
    Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Returns <paramref name="info"/> with <see cref="MediaInfo.HasClosedCaptions"/> set from the
    /// file's first video frames. This decodes a few seconds of video, so call it only for a file
    /// about to be encoded or checked against its encode, never during a scan.
    /// </summary>
    /// <param name="info">The result of <see cref="ProbeAsync"/> for the same file.</param>
    Task<MediaInfo> DetectClosedCaptionsAsync(string path, MediaInfo info, CancellationToken cancellationToken);
}
