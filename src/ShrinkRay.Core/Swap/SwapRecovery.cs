namespace ShrinkRay.Core.Swap;

public enum RecoveryKind
{
    /// <summary>Nothing needed doing.</summary>
    NoAction,

    /// <summary>The original is back under its own name and the file is marked Failed.</summary>
    RolledBack,

    /// <summary>The new file was already in place. Recovery finished the swap.</summary>
    Completed,

    /// <summary>A local step was cut short. The share was not touched and the file is marked Failed.</summary>
    Interrupted,

    /// <summary>A temp file with no journal entry was deleted.</summary>
    DeletedStrayTemp,

    /// <summary>A backup with no journal entry and nothing at the original name was renamed back.</summary>
    RestoredStrayBackup,

    /// <summary>The disk does not match any state recovery can safely fix. Nothing was changed.</summary>
    NeedsReview,
}

public sealed record RecoveryAction(string Path, RecoveryKind Kind, string Description);

/// <summary>Checks a swapped-in file against the backup of the original it replaced.</summary>
public interface IFinalFileCheck
{
    Task<bool> VerifiesAsync(string finalPath, string backupPath, CancellationToken cancellationToken);
}

/// <summary>
/// Brings every file the journal left mid-swap back to a safe state. Run it on startup,
/// before any encode starts. Each branch orders its steps so that a crash during recovery
/// leaves a state the next run handles.
/// </summary>
public sealed class SwapRecovery
{
    private readonly IFileSystem _fs;
    private readonly SwapJournal _journal;
    private readonly IFinalFileCheck _finalCheck;

    public SwapRecovery(IFileSystem fs, SwapJournal journal, IFinalFileCheck finalCheck)
    {
        _fs = fs;
        _journal = journal;
        _finalCheck = finalCheck;
    }

    /// <param name="foldersToScan">Folders searched, with subfolders, for temp and backup files the journal does not explain.</param>
    public async Task<IReadOnlyList<RecoveryAction>> RecoverAsync(IEnumerable<string> foldersToScan, CancellationToken cancellationToken)
    {
        var actions = new List<RecoveryAction>();
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _journal.LatestEntries())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!NeedsAttention(entry))
            {
                continue;
            }

            handled.Add(entry.Path);
            var action = await ResolveAsync(entry.Path, cancellationToken);
            if (action.Kind != RecoveryKind.NoAction)
            {
                actions.Add(action);
            }
        }

        foreach (var folder in foldersToScan.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var backup in _fs.EnumerateFiles(folder, "*" + SwapPaths.BackupSuffix, recursive: true).ToList())
            {
                var original = SwapPaths.OriginalFor(backup);
                if (original is null || handled.Contains(original) || IsMidSwap(original))
                {
                    continue;
                }

                if (_fs.FileExists(original))
                {
                    actions.Add(NeedsReview(original,
                        "A backup with no journal entry sits beside a file of the same name. Nothing was changed."));
                }
                else
                {
                    _fs.MoveFile(backup, original);
                    actions.Add(new RecoveryAction(original, RecoveryKind.RestoredStrayBackup,
                        "A backup with no journal entry was renamed back to the original name."));
                }
            }

            foreach (var temp in _fs.EnumerateFiles(folder, "*" + SwapPaths.TempSuffix, recursive: true).ToList())
            {
                var original = SwapPaths.OriginalFor(temp);
                if (original is null || handled.Contains(original) || IsMidSwap(original))
                {
                    continue;
                }

                _fs.DeleteFile(temp);
                actions.Add(new RecoveryAction(original, RecoveryKind.DeletedStrayTemp, "Deleted a temp file with no journal entry."));
            }
        }

        return actions;
    }

    /// <summary>
    /// Settles one file from its journal state and what is on disk. The swap also calls this to
    /// roll back after an error.
    /// </summary>
    public async Task<RecoveryAction> ResolveAsync(string path, CancellationToken cancellationToken)
    {
        var state = _journal.CurrentState(path);
        if (state is not { } current)
        {
            return new RecoveryAction(path, RecoveryKind.NoAction, "The journal has no entry for this file.");
        }

        if (current.IsLocalStep())
        {
            var message = $"Interrupted at {current}. The share was not touched.";
            _journal.Record(path, SwapState.Failed, message);
            return new RecoveryAction(path, RecoveryKind.Interrupted, message);
        }

        if (!current.IsShareStep())
        {
            return new RecoveryAction(path, RecoveryKind.NoAction, $"Nothing to recover at {current}.");
        }

        var backup = SwapPaths.Backup(path);
        var temp = SwapPaths.Temp(path);
        var hasOriginalName = _fs.FileExists(path);
        var hasBackup = _fs.FileExists(backup);
        var hasTemp = _fs.FileExists(temp);

        if (hasBackup && !hasOriginalName)
        {
            // Temp first. A crash between the two steps then leaves backup-only, handled here.
            DeleteIfPresent(temp, hasTemp);
            _fs.MoveFile(backup, path);
            return RolledBack(path, $"Interrupted at {current}. The original was renamed back.");
        }

        if (hasBackup)
        {
            // Both names exist, so the temp file was already renamed into place.
            if (current is not (SwapState.Swapped or SwapState.OriginalDeleted))
            {
                return NeedsReview(path, $"Both the file and its backup exist at {current}, before the rename. Nothing was changed.");
            }

            if (await _finalCheck.VerifiesAsync(path, backup, cancellationToken))
            {
                _journal.Record(path, SwapState.OriginalDeleted, "Recovery verified the new file and deleted the backup.");
                DeleteIfPresent(temp, hasTemp);
                _fs.DeleteFile(backup);
                return new RecoveryAction(path, RecoveryKind.Completed, "The new file verified. The backup was deleted.");
            }

            // The file under the original name is the new encode, so removing it loses nothing.
            DeleteIfPresent(temp, hasTemp);
            _fs.DeleteFile(path);
            _fs.MoveFile(backup, path);
            return RolledBack(path, "The new file failed verification. The original was renamed back.");
        }

        if (hasTemp)
        {
            if (hasOriginalName && current is SwapState.UploadedTemp or SwapState.OriginalRenamedAside)
            {
                _fs.DeleteFile(temp);
                return RolledBack(path, $"Interrupted at {current}. The temp file was deleted and the original was not touched.");
            }

            if (hasOriginalName && current == SwapState.OriginalDeleted)
            {
                _fs.DeleteFile(temp);
                return new RecoveryAction(path, RecoveryKind.DeletedStrayTemp, "The swap had finished. A leftover temp file was deleted.");
            }

            return NeedsReview(path, hasOriginalName
                ? $"A temp file remains at {current} but the backup is missing. Nothing was changed."
                : $"The original and its backup are missing at {current}. The temp file was kept.");
        }

        if (!hasOriginalName)
        {
            return NeedsReview(path, $"The original, backup and temp file are all missing at {current}.");
        }

        return current switch
        {
            SwapState.UploadedTemp or SwapState.OriginalRenamedAside =>
                RolledBack(path, $"Interrupted at {current}. The original was not touched."),
            SwapState.Swapped => Completed(path, "The new file is in place and the backup is already gone."),
            _ => new RecoveryAction(path, RecoveryKind.NoAction, "The swap finished."),
        };
    }

    private RecoveryAction Completed(string path, string description)
    {
        _journal.Record(path, SwapState.OriginalDeleted, description);
        return new RecoveryAction(path, RecoveryKind.Completed, description);
    }

    private RecoveryAction RolledBack(string path, string description)
    {
        _journal.Record(path, SwapState.Failed, description);
        return new RecoveryAction(path, RecoveryKind.RolledBack, description);
    }

    private static RecoveryAction NeedsReview(string path, string description) =>
        new(path, RecoveryKind.NeedsReview, description);

    private void DeleteIfPresent(string path, bool present)
    {
        if (present)
        {
            _fs.DeleteFile(path);
        }
    }

    private bool IsMidSwap(string original) =>
        _journal.CurrentState(original) is SwapState.UploadedTemp or SwapState.OriginalRenamedAside or SwapState.Swapped;

    private bool NeedsAttention(JournalEntry entry)
    {
        if (entry.State.IsLocalStep())
        {
            return true;
        }

        if (entry.State == SwapState.OriginalDeleted)
        {
            // Normal success, unless the delete was cut short.
            return _fs.FileExists(SwapPaths.Backup(entry.Path)) || _fs.FileExists(SwapPaths.Temp(entry.Path));
        }

        return entry.State.IsShareStep();
    }
}
