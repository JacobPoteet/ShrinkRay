using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Swap;
using ShrinkRay.Core.Tests.Swap;

namespace ShrinkRay.Core.Tests.Pipeline;

/// <summary>
/// The deck's rule for closing mid-run: cancel an episode up to and through the upload, let one
/// past it finish. These tests tie the rule to what the swap really does with a cancel.
/// </summary>
public class EpisodePhaseTests
{
    [Theory]
    [InlineData(EpisodePhase.Probing, false)]
    [InlineData(EpisodePhase.Copying, false)]
    [InlineData(EpisodePhase.Encoding, false)]
    [InlineData(EpisodePhase.RestoringFonts, false)]
    [InlineData(EpisodePhase.Verifying, false)]
    [InlineData(EpisodePhase.Uploading, false)]
    [InlineData(EpisodePhase.Swapping, true)]
    public void Only_the_swap_is_past_the_upload(EpisodePhase phase, bool past)
    {
        Assert.Equal(past, phase.IsPastUpload());
    }

    [Fact]
    public async Task A_cancel_during_the_upload_leaves_the_original_and_settles_cancelled()
    {
        var h = new SwapHarness();
        using var cts = new CancellationTokenSource();

        // The pipeline reports Uploading when the swap reports UploadedTemp.
        var steps = new InlineSteps(step =>
        {
            if (step == SwapState.UploadedTemp)
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Executor.SwapAsync(SwapHarness.Original, h.Source, SwapHarness.Encoded, SwapHarness.Passed, h.Settings, cts.Token, steps));

        Assert.Equal(SwapHarness.OriginalContent, h.Fs.Read(SwapHarness.Original));
        Assert.False(h.TempExists);
        Assert.Equal(SwapState.Cancelled, h.State);
    }

    [Fact]
    public async Task A_cancel_once_the_original_is_renamed_aside_does_not_stop_the_swap()
    {
        var h = new SwapHarness();
        using var cts = new CancellationTokenSource();

        // The pipeline reports Swapping when the swap reports OriginalRenamedAside.
        var steps = new InlineSteps(step =>
        {
            if (step == SwapState.OriginalRenamedAside)
            {
                cts.Cancel();
            }
        });

        var result = await h.Executor.SwapAsync(SwapHarness.Original, h.Source, SwapHarness.Encoded, SwapHarness.Passed, h.Settings, cts.Token, steps);

        Assert.Equal(SwapOutcome.Swapped, result.Outcome);
        Assert.Equal(SwapHarness.NewContent, h.Fs.Read(SwapHarness.Original));
        Assert.False(h.BackupExists);
        Assert.Equal(SwapState.OriginalDeleted, h.State);
    }

    private sealed class InlineSteps(Action<SwapState> report) : IProgress<SwapState>
    {
        public void Report(SwapState value) => report(value);
    }
}
