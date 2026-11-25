using ShrinkRay.Core.Swap;
using ShrinkRay.Core.Tests.Support;

namespace ShrinkRay.Core.Tests.Swap;

/// <summary>
/// Kills the journal compaction after each of its durable operations, restarts, and checks that
/// recovery still ends with one good file and the same state. Compaction makes two durable
/// operations: write the new journal beside the old one, then swap it into place.
/// </summary>
public class CompactionCrashTests
{
    private const int SwapOperations = 8;
    private const int CompactOperations = 2;
    private const int RenameIntoPlace = 6;

    public static TheoryData<int, int> SwapAndCompactCrashPoints()
    {
        var data = new TheoryData<int, int>();
        for (var swap = 0; swap < SwapOperations; swap++)
        {
            for (var compact = 0; compact <= CompactOperations; compact++)
            {
                data.Add(swap, compact);
            }
        }

        return data;
    }

    private static async Task<SwapHarness> CrashedSwapThenRecoveredAsync(int swapCrash)
    {
        var h = new SwapHarness();
        h.Crash.CrashAfter(swapCrash);
        await h.SwapAsync();
        h.Restart();
        await h.RecoverAsync();
        return h;
    }

    [Fact]
    public async Task Compaction_makes_two_durable_operations()
    {
        var h = await CrashedSwapThenRecoveredAsync(int.MaxValue);
        h.Crash.CrashAfter(int.MaxValue);

        h.Journal.Compact();

        Assert.Equal(CompactOperations, h.Crash.Operations);
        Assert.Single(h.Store.Entries);
    }

    [Theory]
    [MemberData(nameof(SwapAndCompactCrashPoints))]
    public async Task Crash_during_compaction_leaves_a_journal_that_recovers_the_same(int swapCrash, int compactCrash)
    {
        var h = await CrashedSwapThenRecoveredAsync(swapCrash);
        var settled = h.State;
        var content = h.Fs.Read(SwapHarness.Original);

        h.Crash.CrashAfter(compactCrash);
        try
        {
            h.Journal.Compact();
        }
        catch (SimulatedCrashException)
        {
        }

        h.Restart();
        var actions = await h.RecoverAsync();

        Assert.Empty(actions);
        Assert.Equal(settled, h.State);
        Assert.Equal(content, h.Fs.Read(SwapHarness.Original));
        Assert.False(h.TempExists);
        Assert.False(h.BackupExists);

        // Either the old journal or the compacted one: never anything in between.
        Assert.Equal(compactCrash >= CompactOperations, h.Store.Entries.Count == 1);
    }

    [Theory]
    [MemberData(nameof(SwapAndCompactCrashPoints))]
    public async Task Compacting_before_recovery_never_loses_what_recovery_needs(int swapCrash, int compactCrash)
    {
        var h = new SwapHarness();
        h.Crash.CrashAfter(swapCrash);
        await h.SwapAsync();

        h.Restart();
        h.Crash.CrashAfter(compactCrash);
        try
        {
            h.Journal.Compact();
        }
        catch (SimulatedCrashException)
        {
        }

        h.Restart();
        var actions = await h.RecoverAsync();

        Assert.DoesNotContain(actions, a => a.Kind == RecoveryKind.NeedsReview);
        Assert.True(h.Fs.FileExists(SwapHarness.Original));
        Assert.False(h.TempExists);
        Assert.False(h.BackupExists);
        var expected = swapCrash >= RenameIntoPlace ? SwapHarness.NewContent : SwapHarness.OriginalContent;
        Assert.Equal(expected, h.Fs.Read(SwapHarness.Original));
        Assert.Equal(swapCrash >= RenameIntoPlace ? SwapState.OriginalDeleted
            : swapCrash <= 2 ? SwapState.Cancelled : SwapState.Failed, h.State);
    }
}
