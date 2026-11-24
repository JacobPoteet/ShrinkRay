using ShrinkRay.Core.Swap;

namespace ShrinkRay.Core.Tests.Swap;

public class SwapRecoveryTests
{
    private const string Original = SwapHarness.Original;
    private static readonly string Temp = SwapPaths.Temp(Original);
    private static readonly string Backup = SwapPaths.Backup(Original);

    private static SwapHarness AtState(params SwapState[] shareStates)
    {
        var h = new SwapHarness();
        foreach (var state in shareStates)
        {
            h.Journal.Record(Original, state);
        }

        return h;
    }

    [Fact]
    public async Task Backup_with_nothing_at_the_original_name_is_renamed_back()
    {
        var h = AtState(SwapState.UploadedTemp, SwapState.OriginalRenamedAside);
        h.Fs.Write(Temp, SwapHarness.NewContent);
        h.Fs.MoveFile(Original, Backup);

        var action = Assert.Single(await h.RecoverAsync());

        Assert.Equal(RecoveryKind.RolledBack, action.Kind);
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
        Assert.False(h.TempExists);
        Assert.False(h.BackupExists);
        Assert.Equal(SwapState.Failed, h.State);
    }

    [Fact]
    public async Task Backup_and_verified_final_file_deletes_the_backup()
    {
        var h = AtState(SwapState.UploadedTemp, SwapState.OriginalRenamedAside, SwapState.Swapped);
        h.Fs.MoveFile(Original, Backup);
        h.Fs.Write(Original, SwapHarness.NewContent);

        var action = Assert.Single(await h.RecoverAsync());

        Assert.Equal(RecoveryKind.Completed, action.Kind);
        Assert.Equal(SwapHarness.NewContent, h.Fs.Read(Original));
        Assert.False(h.BackupExists);
        Assert.Equal(SwapState.OriginalDeleted, h.State);
        Assert.Equal(1, h.Check.Calls);
    }

    [Fact]
    public async Task Backup_and_failing_final_file_restores_the_original()
    {
        var h = AtState(SwapState.UploadedTemp, SwapState.OriginalRenamedAside, SwapState.Swapped);
        h.Check.Result = false;
        h.Fs.MoveFile(Original, Backup);
        h.Fs.Write(Original, SwapHarness.NewContent);

        var action = Assert.Single(await h.RecoverAsync());

        Assert.Equal(RecoveryKind.RolledBack, action.Kind);
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
        Assert.False(h.BackupExists);
    }

    [Fact]
    public async Task Backup_and_file_before_the_rename_step_are_left_for_review()
    {
        var h = AtState(SwapState.UploadedTemp, SwapState.OriginalRenamedAside);
        h.Fs.Write(Backup, "something else");

        var action = Assert.Single(await h.RecoverAsync());

        Assert.Equal(RecoveryKind.NeedsReview, action.Kind);
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
        Assert.True(h.BackupExists);
        Assert.Equal(0, h.Check.Calls);
    }

    [Fact]
    public async Task Interrupted_upload_deletes_the_temp_file()
    {
        var h = AtState(SwapState.UploadedTemp);
        h.Fs.Write(Temp, "half");

        var action = Assert.Single(await h.RecoverAsync());

        Assert.Equal(RecoveryKind.RolledBack, action.Kind);
        Assert.False(h.TempExists);
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
    }

    [Fact]
    public async Task Temp_file_is_kept_when_the_original_is_gone()
    {
        var h = AtState(SwapState.UploadedTemp, SwapState.OriginalRenamedAside, SwapState.Swapped);
        h.Fs.Write(Temp, SwapHarness.NewContent);
        h.Fs.DeleteFile(Original);

        var action = Assert.Single(await h.RecoverAsync());

        Assert.Equal(RecoveryKind.NeedsReview, action.Kind);
        Assert.True(h.TempExists);
    }

    [Fact]
    public async Task All_files_missing_needs_review()
    {
        var h = AtState(SwapState.UploadedTemp, SwapState.OriginalRenamedAside);
        h.Fs.DeleteFile(Original);

        Assert.Equal(RecoveryKind.NeedsReview, Assert.Single(await h.RecoverAsync()).Kind);
    }

    [Fact]
    public async Task Stray_temp_with_no_journal_entry_is_deleted()
    {
        var h = new SwapHarness(verified: false);
        var other = SwapHarness.ShowFolder + @"\Season 02\Example Show S02E01.mkv";
        h.Fs.Write(other, "episode");
        h.Fs.Write(SwapPaths.Temp(other), "stray");

        var action = Assert.Single(await h.RecoverAsync());

        Assert.Equal(RecoveryKind.DeletedStrayTemp, action.Kind);
        Assert.Equal(other, action.Path);
        Assert.False(h.Fs.FileExists(SwapPaths.Temp(other)));
        Assert.Equal("episode", h.Fs.Read(other));
    }

    [Fact]
    public async Task Stray_temp_after_a_finished_swap_is_deleted()
    {
        var h = AtState(SwapState.UploadedTemp, SwapState.OriginalRenamedAside, SwapState.Swapped, SwapState.OriginalDeleted);
        h.Fs.Write(Temp, "stray");

        var actions = await h.RecoverAsync();

        Assert.Equal(RecoveryKind.DeletedStrayTemp, Assert.Single(actions).Kind);
        Assert.Equal(SwapState.OriginalDeleted, h.State);

        Assert.False(h.TempExists);
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
    }

    [Fact]
    public async Task Stray_backup_with_no_original_is_renamed_back()
    {
        var h = new SwapHarness(verified: false);
        h.Fs.MoveFile(Original, Backup);

        var action = Assert.Single(await h.RecoverAsync());

        Assert.Equal(RecoveryKind.RestoredStrayBackup, action.Kind);
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
    }

    [Fact]
    public async Task Stray_backup_beside_a_file_is_never_deleted()
    {
        var h = new SwapHarness(verified: false);
        h.Fs.Write(Backup, "unknown");

        var action = Assert.Single(await h.RecoverAsync());

        Assert.Equal(RecoveryKind.NeedsReview, action.Kind);
        Assert.Equal("unknown", h.Fs.Read(Backup));
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
    }

    [Fact]
    public async Task Strays_outside_the_scanned_folders_are_left_alone()
    {
        var h = new SwapHarness(verified: false);
        var elsewhere = @"X:\TV\Other Show\Other Show S01E01.mkv";
        h.Fs.Write(SwapPaths.Temp(elsewhere), "stray");

        Assert.Empty(await h.RecoverAsync());
        Assert.True(h.Fs.FileExists(SwapPaths.Temp(elsewhere)));
    }

    [Theory]
    [InlineData(SwapState.CopiedToScratch)]
    [InlineData(SwapState.Encoded)]
    [InlineData(SwapState.Verified)]
    public async Task Interrupted_local_step_is_marked_failed_without_touching_the_share(SwapState state)
    {
        var h = new SwapHarness(verified: false);
        h.Journal.Record(Original, SwapState.Queued);
        foreach (var s in new[] { SwapState.CopiedToScratch, SwapState.Encoded, SwapState.Verified })
        {
            h.Journal.Record(Original, s);
            if (s == state)
            {
                break;
            }
        }

        var action = Assert.Single(await h.RecoverAsync());

        Assert.Equal(RecoveryKind.Interrupted, action.Kind);
        Assert.Equal(SwapState.Failed, h.State);
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(Original));
    }

    [Fact]
    public async Task Queued_and_finished_files_are_left_alone()
    {
        var h = new SwapHarness(verified: false);
        h.Journal.Record(Original, SwapState.Queued);
        var done = SwapHarness.ShowFolder + @"\Season 01\Example Show S01E02.mkv";
        h.Fs.Write(done, "already swapped");
        foreach (var s in new[] { SwapState.Queued, SwapState.CopiedToScratch, SwapState.Encoded, SwapState.Verified,
                     SwapState.UploadedTemp, SwapState.OriginalRenamedAside, SwapState.Swapped, SwapState.OriginalDeleted })
        {
            h.Journal.Record(done, s);
        }

        Assert.Empty(await h.RecoverAsync());
        Assert.Equal(SwapState.Queued, h.State);
    }
}
