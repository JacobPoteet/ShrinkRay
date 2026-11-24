using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Verification;

namespace ShrinkRay.Core.Swap;

public enum SwapOutcome
{
    Swapped,
    Failed,
}

/// <param name="Message">Why it failed, or a warning about a swap that otherwise succeeded.</param>
public sealed record SwapResult(SwapOutcome Outcome, string? Message);

/// <summary>
/// Replaces an original on the share with its verified encode:
/// copy to <c>name.shrinkray.tmp</c>, rename the original to <c>name.shrinkray.bak</c>,
/// rename the temp file to the original name, delete the backup. Each step is journaled first.
/// </summary>
public sealed class SwapExecutor
{
    private readonly IFileSystem _fs;
    private readonly SwapJournal _journal;
    private readonly SwapRecovery _recovery;

    public SwapExecutor(IFileSystem fs, SwapJournal journal, SwapRecovery recovery)
    {
        _fs = fs;
        _journal = journal;
        _recovery = recovery;
    }

    /// <inheritdoc cref="SwapAsync(string, string, VerificationResult, ShrinkRaySettings, IProgress{SwapState}?, CancellationToken)"/>
    public Task<SwapResult> SwapAsync(
        string originalPath,
        string verifiedFilePath,
        VerificationResult verification,
        ShrinkRaySettings settings,
        CancellationToken cancellationToken) =>
        SwapAsync(originalPath, verifiedFilePath, verification, settings, null, cancellationToken);

    /// <param name="originalPath">The original file on the share. Its journal state must be Verified.</param>
    /// <param name="verifiedFilePath">The verified encode in the scratch folder.</param>
    /// <param name="steps">Told each share step once the journal holds it, just before the step runs.</param>
    /// <param name="cancellationToken">Honoured during the upload only. Once the original is renamed the swap runs to the end.</param>
    /// <exception cref="InvalidOperationException">Verification did not pass, or replacing originals is turned off.</exception>
    /// <exception cref="OperationCanceledException">Cancelled during the upload. The temp file is removed first.</exception>
    public async Task<SwapResult> SwapAsync(
        string originalPath,
        string verifiedFilePath,
        VerificationResult verification,
        ShrinkRaySettings settings,
        IProgress<SwapState>? steps,
        CancellationToken cancellationToken)
    {
        if (!verification.Passed)
        {
            throw new InvalidOperationException("An original can only be replaced by a file that passed verification.");
        }

        if (!settings.ReplaceOriginals)
        {
            throw new InvalidOperationException("Replacing originals is turned off.");
        }

        var state = _journal.CurrentState(originalPath);
        if (state != SwapState.Verified)
        {
            throw new InvalidOperationException($"The journal has {originalPath} at {state?.ToString() ?? "no state"}, not Verified.");
        }

        var temp = SwapPaths.Temp(originalPath);
        var backup = SwapPaths.Backup(originalPath);

        if (!_fs.FileExists(originalPath))
        {
            return Fail(originalPath, "The original is no longer on the share.");
        }

        if (_fs.FileExists(backup))
        {
            return Fail(originalPath, "A backup from an earlier swap is still beside the original. Run recovery first.");
        }

        try
        {
            _journal.Record(originalPath, SwapState.UploadedTemp);
            steps?.Report(SwapState.UploadedTemp);
            if (_fs.FileExists(temp))
            {
                // Left by an earlier attempt at this file. Temp files are always ShrinkRay's own.
                _fs.DeleteFile(temp);
            }

            await _fs.CopyFileAsync(verifiedFilePath, temp, cancellationToken);
            var expected = _fs.GetFileSize(verifiedFilePath);
            var actual = _fs.GetFileSize(temp);
            if (actual != expected)
            {
                throw new IOException($"The uploaded temp file is {actual} bytes, expected {expected}.");
            }

            _journal.Record(originalPath, SwapState.OriginalRenamedAside);
            steps?.Report(SwapState.OriginalRenamedAside);
            _fs.MoveFile(originalPath, backup);

            _journal.Record(originalPath, SwapState.Swapped);
            steps?.Report(SwapState.Swapped);
            _fs.MoveFile(temp, originalPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Skip or Stop. Once the temp file is gone the original was never touched, so this is no failure.
            await RollBackAsync(originalPath, "Cancelled during upload", SwapState.Cancelled);
            throw;
        }
        catch (OperationCanceledException)
        {
            await RollBackAsync(originalPath, "Cancelled during upload", SwapState.Failed);
            throw;
        }
        catch (Exception ex)
        {
            return await RollBackAsync(originalPath, ex.Message, SwapState.Failed);
        }

        try
        {
            _journal.Record(originalPath, SwapState.OriginalDeleted);
            steps?.Report(SwapState.OriginalDeleted);
            _fs.DeleteFile(backup);
        }
        catch (Exception ex)
        {
            // The new file is in place. Recovery checks it and removes the backup on the next start.
            return new SwapResult(SwapOutcome.Swapped, $"The new file is in place, but the backup was not deleted: {ex.Message}");
        }

        return new SwapResult(SwapOutcome.Swapped, null);
    }

    private SwapResult Fail(string path, string message)
    {
        _journal.Record(path, SwapState.Failed, message);
        return new SwapResult(SwapOutcome.Failed, message);
    }

    private async Task<SwapResult> RollBackAsync(string path, string error, SwapState settleAs)
    {
        try
        {
            var action = await _recovery.ResolveAsync(path, settleAs, CancellationToken.None);
            return new SwapResult(SwapOutcome.Failed, $"{error}. {action.Description}");
        }
        catch (Exception ex)
        {
            return new SwapResult(SwapOutcome.Failed, $"{error}. Rollback also failed ({ex.Message}); recovery will retry on the next start.");
        }
    }
}
