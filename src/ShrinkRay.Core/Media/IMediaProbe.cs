namespace ShrinkRay.Core.Media;

/// <summary>Reads a file's container and streams. The real one runs ffprobe.</summary>
public interface IMediaProbe
{
    Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken);
}
