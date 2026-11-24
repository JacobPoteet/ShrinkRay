namespace ShrinkRay.Core.Verification;

/// <summary>
/// Decodes a whole file and reports whether it decoded cleanly. The real one runs
/// <c>ffmpeg -v error -i file -f null -</c>.
/// </summary>
public interface IDecodeChecker
{
    Task<bool> DecodesCleanlyAsync(string path, CancellationToken cancellationToken);
}
