using ShrinkRay.Core.Media;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Verification;

namespace ShrinkRay.Core.Swap;

/// <summary>
/// Runs the full verification again for recovery: probes both files, decodes the new one, and
/// passes only if <see cref="OutputVerifier"/> would allow the swap.
/// </summary>
public sealed class ProbeFinalFileCheck : IFinalFileCheck
{
    private readonly IFileSystem _fs;
    private readonly IMediaProbe _probe;
    private readonly IDecodeChecker _decoder;
    private readonly ShrinkRaySettings _settings;

    public ProbeFinalFileCheck(IFileSystem fs, IMediaProbe probe, IDecodeChecker decoder, ShrinkRaySettings settings)
    {
        _fs = fs;
        _probe = probe;
        _decoder = decoder;
        _settings = settings;
    }

    public async Task<bool> VerifiesAsync(string finalPath, string backupPath, CancellationToken cancellationToken)
    {
        try
        {
            // The backup keeps the original's name plus a suffix, so probe data names the original.
            // The caption check matters here as in the pipeline: a captioned original's encode has one more subtitle track.
            var backup = await _probe.ProbeAsync(backupPath, cancellationToken);
            var source = await _probe.DetectClosedCaptionsAsync(backupPath, backup, cancellationToken) with { FileName = finalPath };
            var output = await _probe.ProbeAsync(finalPath, cancellationToken);
            var decoded = await _decoder.DecodesCleanlyAsync(finalPath, cancellationToken);
            var input = new VerificationInput(source, output, _fs.GetFileSize(backupPath), _fs.GetFileSize(finalPath), decoded);
            return OutputVerifier.Verify(input, _settings).Passed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
