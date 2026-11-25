using ShrinkRay.App.Tray;
using ShrinkRay.Core.Pipeline;

namespace ShrinkRay.App.Tests;

public class TrayModelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static DeckRig Library()
    {
        var rig = new DeckRig();
        rig.Add("Scrubs", 1, 1);
        rig.Add("Scrubs", 1, 2);
        return rig;
    }

    [Theory]
    [InlineData(false, false, false, TrayState.Idle)]
    [InlineData(false, false, true, TrayState.Failed)]
    [InlineData(true, false, false, TrayState.Encoding)]
    [InlineData(true, false, true, TrayState.Encoding)]
    [InlineData(true, true, false, TrayState.Paused)]
    [InlineData(true, true, true, TrayState.Paused)]
    [InlineData(false, true, false, TrayState.Idle)]
    public void State_follows_the_run_with_paused_ahead_of_encoding_and_failed_only_when_idle(
        bool encoding, bool paused, bool failure, TrayState expected) =>
        Assert.Equal(expected, TrayModel.StateFor(encoding, paused, failure));

    [Fact]
    public void Tooltip_names_the_episode_and_percent_while_encoding()
    {
        Assert.Equal("ShrinkRay", TrayModel.TooltipFor(TrayState.Idle, "", "", ""));
        Assert.Equal("ShrinkRay: S01E04 42.3%", TrayModel.TooltipFor(TrayState.Encoding, "S01E04", " 42.3", ""));
        Assert.Equal("ShrinkRay: paused at S01E04 42.3%", TrayModel.TooltipFor(TrayState.Paused, "S01E04", "42.3", ""));
        Assert.Equal("ShrinkRay: encoding", TrayModel.TooltipFor(TrayState.Encoding, "", "", ""));
        Assert.Equal("ShrinkRay: S01E02: Full decode reported errors",
            TrayModel.TooltipFor(TrayState.Failed, "", "", "S01E02: Full decode reported errors"));
        Assert.Equal("ShrinkRay: last episode failed", TrayModel.TooltipFor(TrayState.Failed, "", "", ""));
    }

    [Fact]
    public void Tooltip_fits_the_127_characters_a_notify_icon_allows()
    {
        var text = TrayModel.TooltipFor(TrayState.Failed, "", "", new string('x', 400));

        Assert.Equal(TrayModel.MaxTooltipLength, text.Length);
        Assert.EndsWith("...", text);
    }

    [Fact]
    public async Task Model_follows_a_run_through_encoding_pause_and_back_to_idle()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        using var model = new TrayModel(vm);
        var changes = 0;
        model.Changed += (_, _) => changes++;

        Assert.Equal(TrayState.Idle, model.State);
        Assert.Equal("ShrinkRay", model.Tooltip);

        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        Assert.Equal(TrayState.Encoding, model.State);
        Assert.Contains("S01E01", model.Tooltip);
        Assert.Contains("%", model.Tooltip);

        vm.PauseCommand.Execute(null);
        Assert.Equal(TrayState.Paused, model.State);
        Assert.StartsWith("ShrinkRay: paused at S01E01", model.Tooltip);

        vm.EncodeCommand.Execute(null);
        Assert.Equal(TrayState.Encoding, model.State);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Equal(TrayState.Idle, model.State);
        Assert.Equal("ShrinkRay", model.Tooltip);
        Assert.True(changes >= 4);
    }

    [Fact]
    public async Task A_failed_episode_marks_the_idle_icon_and_the_tooltip_says_why()
    {
        var rig = Library();
        var failed = DeckRig.Episode("Scrubs", 1, 2);
        rig.Runner.Results[failed] = new PipelineResult(PipelineOutcome.Failed, "Full decode reported errors");
        var vm = await rig.StartedWith("Scrubs");
        using var model = new TrayModel(vm);

        vm.EncodeCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Equal(TrayState.Failed, model.State);
        Assert.Contains("S01E02", model.Tooltip);
        Assert.Contains("Full decode reported errors", model.Tooltip);
    }

    [Fact]
    public void The_stop_item_reads_keep_going_while_armed()
    {
        Assert.Equal("Stop after this episode", TrayModel.StopAfterTextFor(armed: false));
        Assert.Equal("Keep going", TrayModel.StopAfterTextFor(armed: true));
    }

    [Fact]
    public async Task The_stop_item_follows_the_run_arms_the_stop_and_takes_it_back()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        using var model = new TrayModel(vm);
        var changes = 0;
        model.Changed += (_, _) => changes++;

        Assert.False(model.CanStopAfter);
        Assert.Equal("Stop after this episode", model.StopAfterText);

        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        Assert.True(model.CanStopAfter);
        Assert.Equal("Stop after this episode", model.StopAfterText);

        model.ToggleStopAfter();
        Assert.True(vm.IsStopArmed);
        Assert.Equal("Keep going", model.StopAfterText);
        Assert.True(model.CanStopAfter);

        model.ToggleStopAfter();
        Assert.False(vm.IsStopArmed);
        Assert.Equal("Stop after this episode", model.StopAfterText);
        Assert.True(changes >= 3);

        model.ToggleStopAfter();
        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.False(model.CanStopAfter);
        Assert.Equal("Stop after this episode", model.StopAfterText);
        Assert.Single(rig.Runner.Ran);
    }

    [Fact]
    public async Task The_stop_item_is_off_once_a_close_is_confirmed()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        using var model = new TrayModel(vm);
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        Assert.False(vm.RequestClose());
        vm.CloseAfterEpisodeCommand.Execute(null);

        Assert.False(model.CanStopAfter);
        Assert.Equal("Keep going", model.StopAfterText);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    [Fact]
    public async Task A_disposed_model_stops_listening()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        var model = new TrayModel(vm);
        model.Dispose();

        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        Assert.Equal(TrayState.Idle, model.State);
        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
    }
}
