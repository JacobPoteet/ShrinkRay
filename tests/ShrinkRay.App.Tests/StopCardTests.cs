using ShrinkRay.App.ViewModels;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Theme.Controls;

namespace ShrinkRay.App.Tests;

/// <summary>The Stop card: after this episode, stop now, keep going, and the close-window route to the same stop.</summary>
public class StopCardTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static DeckRig Library()
    {
        var rig = new DeckRig();
        rig.Add("Scrubs", 1, 1);
        rig.Add("Scrubs", 1, 2);
        rig.Add("Scrubs", 2, 1);
        return rig;
    }

    private static async Task<(DeckRig Rig, MainViewModel Vm, string First)> EncodingAsync()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        return (rig, vm, first);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Timeout;
        while (!condition() && DateTime.UtcNow < until)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The condition did not come true in time.");
    }

    [Fact]
    public async Task Stop_mid_episode_opens_the_card_and_changes_nothing_yet()
    {
        var (rig, vm, first) = await EncodingAsync();

        vm.StopCommand.Execute(null);

        Assert.True(vm.IsStopCardOpen);
        Assert.False(vm.IsStopArmed);
        Assert.Equal("After this episode", vm.StopPrimaryLabel);
        Assert.Contains("S01E01", vm.StopCardText);
        Assert.False(rig.Runner.Tokens[first].IsCancellationRequested);
        Assert.Equal("Encoding", vm.RunLamp.Label);

        vm.DismissCardCommand.Execute(null);
        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Escape_dismisses_the_card_and_changes_nothing()
    {
        var (rig, vm, first) = await EncodingAsync();
        vm.StopCommand.Execute(null);

        vm.DismissCardCommand.Execute(null);

        Assert.False(vm.IsStopCardOpen);
        Assert.False(vm.IsStopArmed);
        Assert.False(rig.Runner.Tokens[first].IsCancellationRequested);
        Assert.Equal(LampState.Off, vm.StopLamp);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
        Assert.Equal(3, rig.Runner.Ran.Count);
    }

    [Fact]
    public async Task After_this_episode_arms_the_stop_and_the_episode_finishes_then_the_run_ends()
    {
        var (rig, vm, first) = await EncodingAsync();
        vm.StopCommand.Execute(null);

        vm.StopPrimaryCommand.Execute(null);

        Assert.False(vm.IsStopCardOpen);
        Assert.True(vm.IsStopArmed);
        Assert.Equal(LampState.Active, vm.StopLamp);
        Assert.True(vm.StopLampBlink);
        Assert.Equal("Last episode", vm.RunLamp.Label);
        Assert.Equal(LampState.Active, vm.RunLamp.State);
        Assert.True(vm.RunLamp.Blink);
        Assert.False(rig.Runner.Tokens[first].IsCancellationRequested);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Single(rig.Runner.Ran);
        Assert.Equal(TrackKind.Shrunk, vm.Tracks[0].Kind);
        Assert.False(vm.IsEncoding);
        Assert.False(vm.IsStopArmed);
        Assert.Equal(LampState.Off, vm.StopLamp);
        Assert.Equal("Ready", vm.RunLamp.Label);
    }

    [Fact]
    public async Task Stop_again_while_armed_offers_keep_going_and_keep_going_runs_the_rest_of_the_list()
    {
        var (rig, vm, first) = await EncodingAsync();
        vm.StopCommand.Execute(null);
        vm.StopPrimaryCommand.Execute(null);

        vm.StopCommand.Execute(null);

        Assert.True(vm.IsStopCardOpen);
        Assert.Equal("Keep going", vm.StopPrimaryLabel);
        Assert.Contains("S01E01", vm.StopCardText);

        vm.StopPrimaryCommand.Execute(null);

        Assert.False(vm.IsStopCardOpen);
        Assert.False(vm.IsStopArmed);
        Assert.Equal(LampState.Off, vm.StopLamp);
        Assert.Equal("Encoding", vm.RunLamp.Label);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
        Assert.Equal(3, rig.Runner.Ran.Count);
    }

    [Fact]
    public async Task Stop_now_takes_the_stop_path_and_discards_the_encode()
    {
        var (rig, vm, first) = await EncodingAsync();
        vm.StopCommand.Execute(null);

        vm.StopNowCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.True(rig.Runner.Tokens[first].IsCancellationRequested);
        Assert.Single(rig.Runner.Ran);
        Assert.Equal(TrackKind.Queued, vm.Tracks[0].Kind);
        Assert.False(vm.IsEncoding);
        Assert.False(vm.IsStopCardOpen);
    }

    [Fact]
    public async Task Stop_now_while_armed_still_stops_at_once()
    {
        var (rig, vm, first) = await EncodingAsync();
        vm.StopCommand.Execute(null);
        vm.StopPrimaryCommand.Execute(null);
        vm.StopCommand.Execute(null);

        vm.StopNowCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.True(rig.Runner.Tokens[first].IsCancellationRequested);
        Assert.Equal(TrackKind.Queued, vm.Tracks[0].Kind);
        Assert.False(vm.IsStopArmed);
        Assert.Equal("Ready", vm.RunLamp.Label);
    }

    [Fact]
    public async Task Stop_with_nothing_to_finish_acts_at_once_with_no_card()
    {
        var rig = Library();
        rig.Settings.EncodeWindowEnabled = true;
        rig.Now = new DateTime(2026, 11, 24, 12, 0, 0);
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await WaitForAsync(() => vm.RunLamp.Label == "Waiting for window");

        vm.StopCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.False(vm.IsStopCardOpen);
        Assert.False(vm.IsEncoding);
        Assert.Empty(rig.Runner.Ran);
    }

    [Fact]
    public async Task Stop_while_held_before_an_episode_starts_acts_at_once()
    {
        var (rig, vm, first) = await EncodingAsync();
        vm.PauseCommand.Execute(null);
        rig.Runner.Hold[first].SetResult();
        await WaitForAsync(() => vm.Tracks[0].Kind == TrackKind.Shrunk);

        // The track turns Shrunk part way through the update that ends the episode. A click
        // lands after the whole update, once the deck holds no current episode.
        rig.OnUiThread(() =>
        {
            Assert.True(vm.IsPaused);
            vm.StopCommand.Execute(null);
        });
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.False(vm.IsStopCardOpen);
        Assert.Single(rig.Runner.Ran);
        Assert.False(vm.IsEncoding);
    }

    [Fact]
    public async Task Stop_when_nothing_runs_does_nothing()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Scrubs");

        vm.StopCommand.Execute(null);

        Assert.False(vm.IsStopCardOpen);
        Assert.False(vm.IsStopArmed);
    }

    [Fact]
    public async Task A_swap_in_progress_finishes_whatever_is_chosen()
    {
        var (rig, vm, first) = await EncodingAsync();
        rig.Runner.Report(EpisodePhase.Swapping);

        // Stop during the swap has nothing to ask: it arms the stop and leaves the swap alone.
        vm.StopCommand.Execute(null);

        Assert.False(vm.IsStopCardOpen);
        Assert.True(vm.IsStopArmed);
        Assert.False(rig.Runner.Tokens[first].IsCancellationRequested);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
        Assert.Equal(TrackKind.Shrunk, vm.Tracks[0].Kind);
        Assert.Single(rig.Runner.Ran);
    }

    [Fact]
    public async Task Stop_now_from_a_card_opened_before_the_swap_starts_leaves_the_swap_alone()
    {
        var (rig, vm, first) = await EncodingAsync();
        vm.StopCommand.Execute(null);
        rig.Runner.Report(EpisodePhase.Uploading);
        rig.Runner.Report(EpisodePhase.Swapping);

        vm.StopNowCommand.Execute(null);

        Assert.False(rig.Runner.Tokens[first].IsCancellationRequested);
        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
        Assert.Equal(TrackKind.Shrunk, vm.Tracks[0].Kind);
        Assert.Single(rig.Runner.Ran);
    }

    [Fact]
    public async Task The_tray_toggle_arms_and_takes_the_stop_back()
    {
        var (rig, vm, first) = await EncodingAsync();
        Assert.True(vm.ToggleStopAfterCommand.CanExecute(null));

        vm.ToggleStopAfterCommand.Execute(null);
        Assert.True(vm.IsStopArmed);
        Assert.False(vm.IsStopCardOpen);

        vm.ToggleStopAfterCommand.Execute(null);
        Assert.False(vm.IsStopArmed);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
        Assert.Equal(3, rig.Runner.Ran.Count);
    }

    [Fact]
    public async Task The_tray_toggle_is_off_while_nothing_runs()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Scrubs");

        Assert.False(vm.ToggleStopAfterCommand.CanExecute(null));
    }

    // ---- Close after this episode ----

    [Fact]
    public async Task Close_after_this_episode_arms_the_stop_and_closes_when_the_run_ends()
    {
        var (rig, vm, first) = await EncodingAsync();
        var ready = 0;
        vm.CloseReady += (_, _) => ready++;

        Assert.False(vm.RequestClose());
        Assert.True(vm.CanCloseAfterEpisode);
        string? shown = null;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CloseText))
            {
                shown = vm.CloseText;
            }
        };
        vm.CloseAfterEpisodeCommand.Execute(null);

        Assert.True(vm.IsClosing);
        Assert.True(vm.IsStopArmed);
        Assert.Equal("Last episode", vm.RunLamp.Label);
        Assert.Contains("Finishing S01E01", vm.CloseText);

        // The card is told about the final text, not only the one from before the stop was armed.
        Assert.Equal(vm.CloseText, shown);
        Assert.False(rig.Runner.Tokens[first].IsCancellationRequested);
        Assert.False(vm.RequestClose());
        Assert.Equal(0, ready);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Equal(TrackKind.Shrunk, vm.Tracks[0].Kind);
        Assert.Single(rig.Runner.Ran);
        Assert.Equal(1, ready);
        Assert.True(vm.RequestClose());
    }

    [Fact]
    public async Task Close_after_this_episode_is_not_offered_once_a_swap_has_started()
    {
        var (rig, vm, first) = await EncodingAsync();
        Assert.False(vm.RequestClose());
        Assert.True(vm.CanCloseAfterEpisode);

        rig.Runner.Report(EpisodePhase.Swapping);

        Assert.False(vm.CanCloseAfterEpisode);
        Assert.False(vm.CloseAfterEpisodeCommand.CanExecute(null));

        vm.CancelCloseCommand.Execute(null);
        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Closing_asks_instead_of_the_stop_card_when_both_are_wanted()
    {
        var (rig, vm, first) = await EncodingAsync();
        vm.StopCommand.Execute(null);

        Assert.False(vm.RequestClose());

        Assert.False(vm.IsStopCardOpen);
        Assert.True(vm.IsCloseConfirmOpen);

        vm.DismissCardCommand.Execute(null);
        Assert.False(vm.IsCloseConfirmOpen);
        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Stop_and_close_while_armed_drops_the_arm_and_stops_at_once()
    {
        var (rig, vm, first) = await EncodingAsync();
        vm.ToggleStopAfterCommand.Execute(null);
        Assert.False(vm.RequestClose());

        vm.ConfirmCloseCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.True(rig.Runner.Tokens[first].IsCancellationRequested);
        Assert.Equal(TrackKind.Queued, vm.Tracks[0].Kind);
        Assert.False(vm.IsStopArmed);
    }
}
