using ShrinkRay.Core.Swap;
using ShrinkRay.Core.Tests.Support;

namespace ShrinkRay.Core.Tests.Swap;

/// <summary>
/// Another program, such as a download that upgrades an episode, writes to the share during a
/// run. The swap must never delete a file other than the one that was encoded.
/// </summary>
public class SourceChangedTests
{
    private const string Original = SwapHarness.Original;
    private const string Upgrade = "UPGRADED h264 bytes that nobody encoded";

    /// <summary>When the other program writes.</summary>
    public enum Moment
    {
        BeforeTheSwap,
        DuringTheUpload,
        JustBeforeTheRename,
    }

    private static SwapHarness Harness(Moment moment)
    {
        var h = new SwapHarness();
        if (moment == Moment.BeforeTheSwap)
        {
            h.Fs.Write(Original, Upgrade);
        }
        else if (moment == Moment.JustBeforeTheRename)
        {
            h.Fs.BeforeMove = (source, _) =>
            {
                if (source == Original)
                {
                    h.Fs.BeforeMove = null;
                    h.Fs.Write(Original, Upgrade);
                }
            };
        }

        return h;
    }

    private static Task<SwapResult> SwapAsync(SwapHarness h, Moment moment) =>
        h.Executor.SwapAsync(Original, h.Source, SwapHarness.Encoded, SwapHarness.Passed, h.Settings, CancellationToken.None,
            moment != Moment.DuringTheUpload ? null : new Steps(step =>
            {
                if (step == SwapState.UploadedTemp)
                {
                    h.Fs.Write(Original, Upgrade);
                }
            }));

    [Theory]
    [InlineData(Moment.BeforeTheSwap)]
    [InlineData(Moment.DuringTheUpload)]
    [InlineData(Moment.JustBeforeTheRename)]
    public async Task A_file_written_during_the_run_is_left_in_place(Moment moment)
    {
        var h = Harness(moment);

        var result = await SwapAsync(h, moment);

        Assert.Equal(SwapOutcome.SourceChanged, result.Outcome);
        Assert.Equal(Upgrade, h.Fs.Read(Original));
        Assert.False(h.TempExists);
        Assert.False(h.BackupExists);
        Assert.Equal(SwapState.Cancelled, h.State);
        Assert.True(h.Fs.FileExists(SwapHarness.Encoded));
    }

    [Fact]
    public async Task A_file_removed_during_the_upload_leaves_no_temp_file()
    {
        var h = new SwapHarness();

        var result = await h.Executor.SwapAsync(Original, h.Source, SwapHarness.Encoded, SwapHarness.Passed, h.Settings,
            CancellationToken.None, new Steps(step =>
            {
                if (step == SwapState.UploadedTemp)
                {
                    h.Fs.DeleteFile(Original);
                }
            }));

        Assert.Equal(SwapOutcome.SourceChanged, result.Outcome);
        Assert.False(h.Fs.FileExists(Original));
        Assert.False(h.TempExists);
        Assert.Equal(SwapState.Cancelled, h.State);
    }

    // The longest change path renames the new file aside and back: journal, upload, journal,
    // rename, delete temp, rename back, journal, journal, journal. One point past the end too.
    public static TheoryData<Moment, int> CrashPoints()
    {
        var data = new TheoryData<Moment, int>();
        foreach (var moment in Enum.GetValues<Moment>())
        {
            for (var i = 0; i <= 9; i++)
            {
                data.Add(moment, i);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CrashPoints))]
    public async Task A_crash_while_backing_out_keeps_the_new_file(Moment moment, int crashAfter)
    {
        var h = Harness(moment);
        h.Crash.CrashAfter(crashAfter);
        try
        {
            await SwapAsync(h, moment);
        }
        catch (SimulatedCrashException)
        {
        }

        h.Restart();
        var actions = await h.RecoverAsync();

        Assert.DoesNotContain(actions, a => a.Kind == RecoveryKind.NeedsReview);

        // An early crash stops the swap before the other program writes. Either way the file
        // under the name is the one that was there, never the encode of an older one.
        var written = h.Fs.GetStamp(Original) != h.Source;
        Assert.Equal(written ? Upgrade : SwapHarness.OriginalContent, h.Fs.Read(Original));
        Assert.False(h.TempExists);
        Assert.False(h.BackupExists);
        h.Restart();
        Assert.Empty(await h.RecoverAsync());
    }

    [Fact]
    public async Task A_file_that_appears_under_the_name_mid_swap_is_never_deleted()
    {
        var h = new SwapHarness();
        h.Check.Result = false;
        h.Fs.BeforeMove = (source, destination) =>
        {
            if (source == SwapPaths.Temp(Original))
            {
                h.Fs.BeforeMove = null;
                h.Fs.Write(destination, Upgrade);
            }
        };

        var result = await h.SwapAsync();
        h.Restart();
        var actions = await h.RecoverAsync();

        Assert.Equal(SwapOutcome.Failed, result.Outcome);
        Assert.Equal(RecoveryKind.NeedsReview, Assert.Single(actions).Kind);
        Assert.Equal(Upgrade, h.Fs.Read(Original));
        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(SwapPaths.Backup(Original)));
        Assert.Equal(SwapHarness.NewContent, h.Fs.Read(SwapPaths.Temp(Original)));
    }

    private sealed class Steps(Action<SwapState> report) : IProgress<SwapState>
    {
        public void Report(SwapState value) => report(value);
    }
}
