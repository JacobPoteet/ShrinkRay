using ShrinkRay.Core.Library;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Verification;

namespace ShrinkRay.Core.Swap;

public enum SwapOutcome
{
    Swapped,
    Failed,

    /// <summary>
    /// The file on the share is no longer the one that was encoded, so nothing was replaced and
    /// the file is marked Cancelled.
    /// </summary>
    SourceChanged,
}

/// <param name="Message">Why it failed, or a warning about a swap that otherwise succeeded.</param>
public sealed record SwapResult(SwapOutcome Outcome, string? Message);

/// <summary>
/// Replaces an original on the share with its verified encode:
/// copy to <c>name.shrinkray.tmp</c>, rename the original to <c>name.shrinkray.bak</c>,
/// rename the temp file to the original name, delete the backup. Each step is journaled first.
/// The original must still carry the size and write time it had when it was copied for encoding.
/// </summary>
public sealed class SwapExecutor
{
    public const string SourceChangedReason =
        "The file changed on the share during the run. Nothing was replaced; the next run encodes the file as it is now.";

    private readonly IFileSystem _fs;
    private readonly SwapJournal _journal;
    private readonly SwapRecovery _recovery;

    public SwapExecutor(IFileSystem fs, SwapJournal journal, SwapRecovery recovery)
    {
        _fs = fs;
        _journal = journal;
        _recovery = recovery;
    }

    /// <param name="originalPath">The original file on the share. Its journal state must be Verified.</param>
    /// <param name="source">The original's size and write time as it was copied for encoding.</param>
    /// <param name="verifiedFilePath">The verified encode in the scratch folder.</param>
    /// <param name="steps">Told each share step once the journal holds it, just before the step runs.</param>
    /// <param name="uploaded">Told the bytes of the temp file written so far during the upload.</param>
    /// <param name="cancellationToken">Honoured during the upload only. Once the original is renamed the swap runs to the end.</param>
    /// <exception cref="InvalidOperationException">Verification did not pass, or replacing originals is turned off.</exception>
    /// <exception cref="OperationCanceledException">Cancelled during the upload. The temp file is removed first.</exception>
    public async Task<SwapResult> SwapAsync(
        string originalPath,
        FileStamp source,
        string verifiedFilePath,
        VerificationResult verification,
        ShrinkRaySettings settings,
        CancellationToken cancellationToken,
        IProgress<SwapState>? steps = null,
        IProgress<long>? uploaded = null)
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
            // Checked before the upload, so a changed file does not cost one.
            if (_fs.GetStamp(originalPath) != source)
            {
                _journal.Record(originalPath, SwapState.Cancelled, SourceChangedReason);
                return new SwapResult(SwapOutcome.SourceChanged, SourceChangedReason);
            }

            _journal.Record(originalPath, SwapState.UploadedTemp);
            steps?.Report(SwapState.UploadedTemp);
            if (_fs.FileExists(temp))
            {
                // Left by an earlier attempt at this file. Temp files are always ShrinkRay's own.
                _fs.DeleteFile(temp);
            }

            await _fs.CopyFileAsync(verifiedFilePath, temp, uploaded, cancellationToken);
            var expected = _fs.GetFileSize(verifiedFilePath);
            var actual = _fs.GetFileSize(temp);
            if (actual != expected)
            {
                throw new IOException($"The uploaded temp file is {actual} bytes, expected {expected}.");
            }

            // The upload takes a while, so the file is checked again before it is renamed.
            if (!_fs.FileExists(originalPath) || _fs.GetStamp(originalPath) != source)
            {
                // Journaled first: a crash here leaves a stray temp file, which startup deletes.
                _journal.Record(originalPath, SwapState.Cancelled, SourceChangedReason);
                TryDelete(temp);
                return new SwapResult(SwapOutcome.SourceChanged, SourceChangedReason);
            }

            _journal.Record(originalPath, SwapState.OriginalRenamedAside);
            steps?.Report(SwapState.OriginalRenamedAside);
            _fs.MoveFile(originalPath, backup);

            // The backup is the file this swap goes on to delete. Under its new name nothing else
            // writes to it, so this last look cannot be overtaken.
            if (_fs.GetStamp(backup) != source)
            {
                var action = await _recovery.ResolveAsync(originalPath, CancellationToken.None);
                if (action.Kind != RecoveryKind.RolledBack)
                {
                    return new SwapResult(SwapOutcome.Failed, $"{SourceChangedReason} {action.Description}");
                }

                // The rollback settles as Failed, since a share step may not end Cancelled. The
                // file has its name back and nothing went wrong, so it goes round to Cancelled.
                _journal.Record(originalPath, SwapState.Queued);
                _journal.Record(originalPath, SwapState.Cancelled, SourceChangedReason);
                return new SwapResult(SwapOutcome.SourceChanged, SourceChangedReason);
            }

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

    private void TryDelete(string path)
    {
        try
        {
            _fs.DeleteFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Startup recovery deletes a temp file no swap is using.
        }
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
