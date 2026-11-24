using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Swap;
using ShrinkRay.Core.Verification;

namespace ShrinkRay.Core.Tests.Swap;

public class SwapExecutorTests
{
    private const string Original = SwapHarness.Original;

    [Fact]
    public async Task Swap_puts_the_new_file_under_the_original_name()
    {
        var h = new SwapHarness();

        var result = await h.SwapAsync();

        Assert.Equal(SwapOutcome.Swapped, result.Outcome);
        Assert.Null(result.Message);
        Assert.Equal(SwapHarness.NewContent, h.Fs.Read(Original));
        Assert.False(h.TempExists);
        Assert.False(h.BackupExists);
        Assert.True(h.Fs.FileExists(SwapHarness.Encoded));
    }

    [Fact]
    public async Task Swap_journals_each_step_in_order()
    {
        var h = new SwapHarness();

        await h.SwapAsync();

        Assert.Equal(
            [
                SwapState.Queued, SwapState.CopiedToScratch, SwapState.Encoded, SwapState.Verified,
                SwapState.UploadedTemp, SwapState.OriginalRenamedAside, SwapState.Swapped, SwapState.OriginalDeleted,
            ],
            h.Store.StatesFor(Original));
    }

    [Fact]
    public async Task Each_step_is_journaled_before_it_runs()
    {
        var h = new SwapHarness();
        var log = new List<string>();
        var observer = new ObservingFileSystem(h.Fs, log, h.Store);
        var executor = new SwapExecutor(observer, h.Journal, h.Recovery);

        await executor.SwapAsync(Original, SwapHarness.Encoded, SwapHarness.Passed, h.Settings, CancellationToken.None);

        Assert.Equal(
            [
                "copy after UploadedTemp",
                "move original to backup after OriginalRenamedAside",
                "move temp to original after Swapped",
                "delete backup after OriginalDeleted",
            ],
            log);
    }

    [Fact]
    public async Task Swap_refuses_a_file_that_did_not_pass_verification()
    {
        var h = new SwapHarness();
        var keep = new VerificationResult(VerificationOutcome.KeepOriginal, ["Saves 5.0%"], 0.05);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Executor.SwapAsync(Original, SwapHarness.Encoded, keep, h.Settings, CancellationToken.None));

        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
        Assert.Equal(SwapState.Verified, h.State);
    }

    [Fact]
    public async Task Swap_refuses_when_replace_originals_is_off()
    {
        var h = new SwapHarness();
        var settings = new ShrinkRaySettings { ReplaceOriginals = false };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Executor.SwapAsync(Original, SwapHarness.Encoded, SwapHarness.Passed, settings, CancellationToken.None));

        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
        Assert.False(h.TempExists);
    }

    [Fact]
    public async Task Swap_requires_the_verified_state()
    {
        var h = new SwapHarness(verified: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.SwapAsync());
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
    }

    [Fact]
    public async Task Existing_backup_stops_the_swap()
    {
        var h = new SwapHarness();
        h.Fs.Write(SwapPaths.Backup(Original), "older original");

        var result = await h.SwapAsync();

        Assert.Equal(SwapOutcome.Failed, result.Outcome);
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
        Assert.Equal("older original", h.Fs.Read(SwapPaths.Backup(Original)));
        Assert.Equal(SwapState.Failed, h.State);
    }

    [Fact]
    public async Task Missing_original_fails_without_writing_anything()
    {
        var h = new SwapHarness();
        h.Fs.DeleteFile(Original);

        var result = await h.SwapAsync();

        Assert.Equal(SwapOutcome.Failed, result.Outcome);
        Assert.False(h.TempExists);
        Assert.False(h.Fs.FileExists(Original));
    }

    [Fact]
    public async Task Leftover_temp_file_is_replaced()
    {
        var h = new SwapHarness();
        h.Fs.Write(SwapPaths.Temp(Original), "partial");

        var result = await h.SwapAsync();

        Assert.Equal(SwapOutcome.Swapped, result.Outcome);
        Assert.Equal(SwapHarness.NewContent, h.Fs.Read(Original));
    }

    [Fact]
    public async Task Locked_original_rolls_back_and_keeps_the_original()
    {
        var h = new SwapHarness();
        h.Fs.FailNextMoveFrom = (Original, new IOException("The file is in use"));

        var result = await h.SwapAsync();

        Assert.Equal(SwapOutcome.Failed, result.Outcome);
        Assert.Contains("The file is in use", result.Message);
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
        Assert.False(h.TempExists);
        Assert.False(h.BackupExists);
        Assert.Equal(SwapState.Failed, h.State);
    }

    [Fact]
    public async Task Failed_rename_into_place_restores_the_original()
    {
        var h = new SwapHarness();
        h.Fs.FailNextMoveFrom = (SwapPaths.Temp(Original), new IOException("Access denied"));

        var result = await h.SwapAsync();

        Assert.Equal(SwapOutcome.Failed, result.Outcome);
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
        Assert.False(h.TempExists);
        Assert.False(h.BackupExists);
        Assert.Equal(SwapState.Failed, h.State);
    }

    [Fact]
    public async Task Cancelled_upload_removes_the_temp_file_and_rethrows()
    {
        var h = new SwapHarness();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.SwapAsync(cts.Token));

        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
        Assert.False(h.TempExists);
        Assert.Equal(SwapState.Cancelled, h.State);
    }

    [Fact]
    public async Task Short_upload_is_caught_and_rolled_back()
    {
        var h = new SwapHarness();
        var shortCopy = new ShortCopyFileSystem(h.Fs);
        var executor = new SwapExecutor(shortCopy, h.Journal, new SwapRecovery(shortCopy, h.Journal, h.Check));

        var result = await executor.SwapAsync(Original, SwapHarness.Encoded, SwapHarness.Passed, h.Settings, CancellationToken.None);

        Assert.Equal(SwapOutcome.Failed, result.Outcome);
        Assert.Contains("expected", result.Message);
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
        Assert.False(h.TempExists);
    }

    [Fact]
    public async Task Failed_backup_delete_still_counts_as_swapped()
    {
        var h = new SwapHarness();
        var stubborn = new UndeletableBackupFileSystem(h.Fs);
        var executor = new SwapExecutor(stubborn, h.Journal, h.Recovery);

        var result = await executor.SwapAsync(Original, SwapHarness.Encoded, SwapHarness.Passed, h.Settings, CancellationToken.None);

        Assert.Equal(SwapOutcome.Swapped, result.Outcome);
        Assert.Contains("backup was not deleted", result.Message);
        Assert.Equal(SwapHarness.NewContent, h.Fs.Read(Original));
        Assert.True(h.BackupExists);

        // The next start finishes the job.
        h.Restart();
        var actions = await h.RecoverAsync();
        Assert.Equal(RecoveryKind.Completed, Assert.Single(actions).Kind);
        Assert.False(h.BackupExists);
    }

    private class DelegatingFileSystem(IFileSystem inner) : IFileSystem
    {
        public virtual bool FileExists(string path) => inner.FileExists(path);
        public virtual long GetFileSize(string path) => inner.GetFileSize(path);
        public virtual void MoveFile(string source, string destination) => inner.MoveFile(source, destination);
        public virtual void DeleteFile(string path) => inner.DeleteFile(path);
        public virtual Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken) =>
            inner.CopyFileAsync(source, destination, cancellationToken);
        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) =>
            inner.EnumerateFiles(directory, searchPattern, recursive);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public void DeleteDirectory(string path) => inner.DeleteDirectory(path);
    }

    private sealed class ShortCopyFileSystem(IFileSystem inner) : DelegatingFileSystem(inner)
    {
        public override long GetFileSize(string path) =>
            path.EndsWith(SwapPaths.TempSuffix, StringComparison.Ordinal) ? base.GetFileSize(path) - 1 : base.GetFileSize(path);
    }

    private sealed class UndeletableBackupFileSystem(IFileSystem inner) : DelegatingFileSystem(inner)
    {
        public override void DeleteFile(string path)
        {
            if (path.EndsWith(SwapPaths.BackupSuffix, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("Access denied");
            }

            base.DeleteFile(path);
        }
    }

    /// <summary>Logs each share step with the journal state that was on disk when it ran.</summary>
    private sealed class ObservingFileSystem(IFileSystem inner, List<string> log, Support.InMemoryJournalStore store) : DelegatingFileSystem(inner)
    {
        private string Last => store.Entries[^1].State.ToString();

        public override Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
        {
            log.Add($"copy after {Last}");
            return base.CopyFileAsync(source, destination, cancellationToken);
        }

        public override void MoveFile(string source, string destination)
        {
            var what = destination.EndsWith(SwapPaths.BackupSuffix, StringComparison.Ordinal) ? "original to backup" : "temp to original";
            log.Add($"move {what} after {Last}");
            base.MoveFile(source, destination);
        }

        public override void DeleteFile(string path)
        {
            if (path.EndsWith(SwapPaths.BackupSuffix, StringComparison.Ordinal))
            {
                log.Add($"delete backup after {Last}");
            }

            base.DeleteFile(path);
        }
    }
}
