using ShrinkRay.Core.Swap;

namespace ShrinkRay.Core.Tests.Swap;

/// <summary>
/// Kills the swap after every possible number of durable operations, restarts, runs recovery,
/// and checks that the share holds exactly one good file under the original name.
/// The swap makes eight durable operations: journal, copy, journal, rename original aside,
/// journal, rename temp into place, journal, delete backup.
/// </summary>
public class CrashRecoveryTests
{
    private const int SwapOperations = 8;

    // A cancelled upload journals UploadedTemp, then its rollback journals Cancelled.
    private const int CancelOperations = 2;

    // The sixth operation renames the temp file into place. From then on the new file wins.
    private const int RenameIntoPlace = 6;

    public static TheoryData<int> CrashPoints()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < SwapOperations; i++)
        {
            data.Add(i);
        }

        return data;
    }

    public static TheoryData<int, int> DoubleCrashPoints()
    {
        var data = new TheoryData<int, int>();
        for (var swap = 0; swap < SwapOperations; swap++)
        {
            for (var recovery = 0; recovery < 4; recovery++)
            {
                data.Add(swap, recovery);
            }
        }

        return data;
    }

    [Fact]
    public async Task Swap_without_a_crash_makes_eight_durable_operations()
    {
        var h = new SwapHarness();
        h.Crash.CrashAfter(int.MaxValue);

        await h.SwapAsync();

        Assert.Equal(SwapOperations, h.Crash.Operations);
    }

    [Theory]
    [MemberData(nameof(CrashPoints))]
    public async Task Crash_at_any_point_recovers_to_one_good_file(int crashAfter)
    {
        var h = new SwapHarness();
        h.Crash.CrashAfter(crashAfter);
        await h.SwapAsync();

        h.Restart();
        var actions = await h.RecoverAsync();

        AssertClean(h);
        Assert.DoesNotContain(actions, a => a.Kind == RecoveryKind.NeedsReview);
        if (crashAfter >= RenameIntoPlace)
        {
            Assert.Equal(SwapHarness.NewContent, h.Fs.Read(SwapHarness.Original));
            Assert.Equal(SwapState.OriginalDeleted, h.State);
        }
        else
        {
            Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(SwapHarness.Original));
            Assert.Equal(SwapState.Failed, h.State);
        }
    }

    [Theory]
    [MemberData(nameof(CrashPoints))]
    public async Task New_file_that_fails_its_check_is_rolled_back(int crashAfter)
    {
        var h = new SwapHarness();
        h.Check.Result = false;
        h.Crash.CrashAfter(crashAfter);
        await h.SwapAsync();

        h.Restart();
        await h.RecoverAsync();

        AssertClean(h);
        if (crashAfter >= RenameIntoPlace)
        {
            // Backup and new file both existed: the check decided, and it said no.
            Assert.Equal(1, h.Check.Calls);
        }

        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(SwapHarness.Original));
        Assert.Equal(SwapState.Failed, h.State);
    }

    [Theory]
    [MemberData(nameof(DoubleCrashPoints))]
    public async Task Crash_during_recovery_is_recovered_on_the_next_start(int swapCrash, int recoveryCrash)
    {
        var h = new SwapHarness();
        h.Crash.CrashAfter(swapCrash);
        await h.SwapAsync();

        h.Restart();
        h.Crash.CrashAfter(recoveryCrash);
        try
        {
            await h.RecoverAsync();
        }
        catch (Support.SimulatedCrashException)
        {
        }

        h.Restart();
        var actions = await h.RecoverAsync();

        AssertClean(h);
        Assert.DoesNotContain(actions, a => a.Kind == RecoveryKind.NeedsReview);
        var expected = swapCrash >= RenameIntoPlace ? SwapHarness.NewContent : SwapHarness.OriginalContent;
        Assert.Equal(expected, h.Fs.Read(SwapHarness.Original));
    }

    [Theory]
    [MemberData(nameof(CrashPoints))]
    public async Task Recovery_twice_changes_nothing_the_second_time(int crashAfter)
    {
        var h = new SwapHarness();
        h.Crash.CrashAfter(crashAfter);
        await h.SwapAsync();
        h.Restart();
        await h.RecoverAsync();
        var entries = h.Store.Entries.Count;

        h.Restart();
        var actions = await h.RecoverAsync();

        Assert.Empty(actions);
        Assert.Equal(entries, h.Store.Entries.Count);
    }

    [Theory]
    [MemberData(nameof(CancelCrashPoints))]
    public async Task Crash_while_a_cancelled_upload_rolls_back_recovers_to_the_original(int crashAfter)
    {
        var h = new SwapHarness();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        h.Crash.CrashAfter(crashAfter);
        try
        {
            await h.SwapAsync(stop.Token);
        }
        catch (OperationCanceledException)
        {
        }

        h.Restart();
        var actions = await h.RecoverAsync();

        AssertClean(h);
        Assert.DoesNotContain(actions, a => a.Kind == RecoveryKind.NeedsReview);
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(SwapHarness.Original));

        // Only a rollback that finished counts as cancelled; one a crash cut short is a failure.
        Assert.Equal(crashAfter >= CancelOperations ? SwapState.Cancelled : SwapState.Failed, h.State);
        h.Restart();
        Assert.Empty(await h.RecoverAsync());
    }

    public static TheoryData<int> CancelCrashPoints()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i <= CancelOperations; i++)
        {
            data.Add(i);
        }

        return data;
    }

    private static void AssertClean(SwapHarness h)
    {
        Assert.True(h.Fs.FileExists(SwapHarness.Original), "Nothing under the original name");
        Assert.False(h.TempExists, "Temp file left behind");
        Assert.False(h.BackupExists, "Backup left behind");
    }
}
