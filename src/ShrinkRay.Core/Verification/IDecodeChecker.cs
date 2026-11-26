namespace ShrinkRay.Core.Verification;

/// <summary>
/// Decodes a whole file and reports whether it decoded cleanly. The real one runs
/// <c>ffmpeg -v error -i file -f null -</c>.
/// </summary>
public interface IDecodeChecker
{
    /// <param name="decoded">Told now and then how much of the file's running time has decoded.</param>
    Task<bool> DecodesCleanlyAsync(string path, CancellationToken cancellationToken, IProgress<TimeSpan>? decoded = null);
}
