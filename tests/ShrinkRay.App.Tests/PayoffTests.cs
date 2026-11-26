using ShrinkRay.App.ViewModels;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Theme.Controls;

namespace ShrinkRay.App.Tests;

public class PayoffTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>A two-episode show with the second episode held, so the first has just finished.</summary>
    private static async Task<(DeckRig Rig, MainViewModel Vm, string Second)> AfterTheFirstAsync(PipelineResult? first = null)
    {
        var rig = new DeckRig();
        var one = rig.Add("Payshow", 1, 1);
        var two = rig.Add("Payshow", 1, 2);
        rig.Add("Othershow", 1, 1);
        if (first is not null)
        {
            rig.Runner.Results[one] = first;
        }

        rig.Runner.Hold[two] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Payshow");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        return (rig, vm, two);
    }

    private static async Task FinishAsync(DeckRig rig, MainViewModel vm, string held)
    {
        rig.Runner.Hold[held].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    [Fact]
    public async Task A_replaced_file_stamps_its_row_lights_the_lamp_and_rolls_both_totals_from_their_old_readings()
    {
        var (rig, vm, second) = await AfterTheFirstAsync();

        Assert.True(vm.IsBanked);
        Assert.Equal(LampState.Ok, vm.BankedLamp);
        Assert.True(vm.Tracks[0].IsStamped);
        Assert.True(vm.Tracks[0].IsStamping);
        Assert.Equal("  0.0", vm.SavedRoll!.From);
        Assert.Equal("  0.0", vm.LifetimeSavedRoll!.From);
        Assert.Equal("  0.6", vm.SavedDigits);

        await FinishAsync(rig, vm, second);

        // The second replace asks for a new roll, from where the first one ended.
        Assert.Equal("  0.6", vm.SavedRoll!.From);
        Assert.False(vm.Tracks[0].IsStamping);
        Assert.True(vm.Tracks[0].IsStamped);
        Assert.True(vm.Tracks[1].IsStamping);
    }

    [Fact]
    public async Task The_lamp_goes_out_after_six_seconds_and_the_stamp_stays_on_the_row()
    {
        var (rig, vm, second) = await AfterTheFirstAsync();

        rig.Now = rig.Now.AddSeconds(5.5);
        vm.Tick();
        Assert.True(vm.IsBanked);

        rig.Now = rig.Now.AddSeconds(0.5);
        vm.Tick();
        Assert.False(vm.IsBanked);
        Assert.Equal(LampState.Off, vm.BankedLamp);
        Assert.False(vm.Tracks[0].IsStamping);
        Assert.True(vm.Tracks[0].IsStamped);

        await FinishAsync(rig, vm, second);
    }

    [Fact]
    public async Task A_kept_original_pays_nothing()
    {
        var (rig, vm, second) = await AfterTheFirstAsync(new PipelineResult(PipelineOutcome.KeptOriginal, "Saves 9%"));

        Assert.False(vm.IsBanked);
        Assert.Null(vm.SavedRoll);
        Assert.Null(vm.LifetimeSavedRoll);
        Assert.False(vm.Tracks[0].IsStamped);

        await FinishAsync(rig, vm, second);
    }

    [Fact]
    public async Task Loading_another_show_clears_the_payoff()
    {
        var (rig, vm, second) = await AfterTheFirstAsync();
        await FinishAsync(rig, vm, second);

        vm.SelectedShow = vm.Shows.Single(s => s.FolderName == "Othershow");

        Assert.False(vm.IsBanked);
        Assert.Null(vm.SavedRoll);
        Assert.Null(vm.LifetimeSavedRoll);
    }

    [Fact]
    public async Task The_episode_bar_and_the_head_lamp_follow_the_encode()
    {
        var (rig, vm, second) = await AfterTheFirstAsync();

        Assert.Equal(ScriptedRunner.Timing.Fraction(EpisodePhase.Encoding, 0.5), vm.EpisodeFraction, 6);
        Assert.Equal(LampState.Active, vm.HeadLamp);

        vm.PauseCommand.Execute(null);
        Assert.Equal(LampState.Off, vm.HeadLamp);
        vm.PauseCommand.Execute(null);

        rig.Runner.Report(EpisodePhase.Verifying);
        Assert.Equal(ScriptedRunner.Timing.Fraction(EpisodePhase.Verifying, 0), vm.EpisodeFraction, 6);
        Assert.Equal(LampState.Off, vm.HeadLamp);

        await FinishAsync(rig, vm, second);
        Assert.Equal(0, vm.EpisodeFraction);
        Assert.Equal(LampState.Off, vm.HeadLamp);
    }
}
