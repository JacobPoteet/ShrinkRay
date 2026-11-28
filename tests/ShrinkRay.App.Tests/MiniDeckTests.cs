using System.Windows;
using ShrinkRay.App.Controls;
using ShrinkRay.App.Services;
using ShrinkRay.App.ViewModels;
using ShrinkRay.Theme.Controls;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.App.Tests;

/// <summary>The mini cassette: its keys, its run time, its reels and its lamp.</summary>
public class MiniDeckTests
{
    private static ShowRowViewModel Row(MainViewModel vm, string show) => vm.Shows.Single(s => s.FolderName == show);

    private static async Task<MainViewModel> EmptyAsync(DeckRig rig)
    {
        var vm = rig.Create();
        await vm.StartAsync();
        return vm;
    }

    private static int Minutes(string hours, string minutes) => int.Parse(hours.Trim()) * 60 + int.Parse(minutes);

    private static async Task<(DeckRig Rig, MainViewModel Vm)> EncodingAsync(double durationSeconds = 1320)
    {
        var rig = new DeckRig();
        var first = rig.Add("Longshow", 1, 1);
        rig.Probe.Results[first] = rig.Probe.Results[first] with { DurationSeconds = durationSeconds };
        rig.Add("Longshow", 1, 2);
        rig.Add("Sintel", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Longshow");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        return (rig, vm);
    }

    [Fact]
    public async Task Mini_key_and_Open_key_swap_the_deck_and_the_mini()
    {
        var vm = await EmptyAsync(new DeckRig());

        Assert.False(vm.IsMini);
        vm.ShowMiniCommand.Execute(null);
        Assert.True(vm.IsMini);
        vm.ShowDeckCommand.Execute(null);
        Assert.False(vm.IsMini);
    }

    [Fact]
    public async Task Run_left_reads_dashes_with_nothing_loaded_or_queued()
    {
        var vm = await EmptyAsync(new DeckRig());

        Assert.Equal("--", vm.RunLeftHourDigits);
        Assert.Equal("--", vm.RunLeftMinuteDigits);
        Assert.Equal("Run time left, none", vm.RunLeftName);
    }

    [Fact]
    public async Task Run_left_matches_the_show_counter_when_nothing_is_queued()
    {
        var (rig, vm) = await EncodingAsync();
        rig.Runner.Report(20, 120);
        vm.Tick();

        Assert.Equal(Minutes(vm.ShowLeftHourDigits, vm.ShowLeftMinuteDigits), vm.RunLeftMinutes);
        rig.Runner.Hold.Values.First().SetResult();
    }

    [Fact]
    public async Task Run_left_adds_the_queued_shows()
    {
        var (rig, vm) = await EncodingAsync();
        var showOnly = vm.RunLeftMinutes;

        Row(vm, "Sintel").QueueCommand!.Execute(null);

        Assert.True(vm.RunLeftMinutes > showOnly);
        Assert.Contains("1 show queued", vm.RunLeftDetail);
        rig.Runner.Hold.Values.First().SetResult();
    }

    [Fact]
    public async Task Run_left_goes_past_the_deck_counters_cap()
    {
        // About 1,400 hours of video: the deck stops at 99:59, the mini keeps counting.
        var (rig, vm) = await EncodingAsync(durationSeconds: 5_000_000);

        Assert.Equal("99", vm.ShowLeftHourDigits);
        Assert.Equal("59", vm.ShowLeftMinuteDigits);
        Assert.True(vm.RunLeftMinutes > 100 * 60);
        Assert.Equal((vm.RunLeftMinutes / 60).ToString(), vm.RunLeftHourDigits.Trim());
        rig.Runner.Hold.Values.First().SetResult();
    }

    [Fact]
    public async Task Reels_turn_at_their_usual_speed_until_the_encoder_reports_one()
    {
        var (rig, vm) = await EncodingAsync();

        Assert.Equal(TapeDeckMath.SourceSecondsPerTurn, vm.SourceSecondsPerTurn);
        Assert.Equal(TapeDeckMath.OutputSecondsPerTurn, vm.OutputSecondsPerTurn);
        rig.Runner.Hold.Values.First().SetResult();
    }

    [Fact]
    public async Task Reels_speed_up_with_a_fast_encoder_and_never_past_double()
    {
        var (rig, vm) = await EncodingAsync();
        rig.Runner.Report(10, 100_000);

        Assert.Equal(TapeDeckMath.SourceSecondsPerTurn / ReelMath.MaxSpeedUp, vm.SourceSecondsPerTurn, 6);
        Assert.Equal(TapeDeckMath.OutputSecondsPerTurn / ReelMath.MaxSpeedUp, vm.OutputSecondsPerTurn, 6);
        rig.Runner.Hold.Values.First().SetResult();
    }

    [Fact]
    public async Task Mini_lamp_shows_the_run_in_phosphor_never_red()
    {
        var (rig, vm) = await EncodingAsync();

        Assert.Equal(LampState.Red, vm.RunLamp.State);
        Assert.Equal(LampState.Active, vm.MiniLampState);
        rig.Runner.Hold.Values.First().SetResult();
    }

    [Fact]
    public async Task Moving_the_mini_saves_its_place_for_the_next_start()
    {
        var rig = new DeckRig();
        var vm = await EmptyAsync(rig);
        Assert.Null(vm.MiniPosition);

        vm.MoveMini(1200, 640);

        Assert.Equal((1200d, 640d), vm.MiniPosition);
        Assert.Equal(1200, rig.SavedPreferences?.MiniLeft);
        Assert.Equal(640, rig.SavedPreferences?.MiniTop);
        Assert.Equal((1200d, 640d), rig.Create(rig.SavedPreferences).MiniPosition);
    }

    [Fact]
    public async Task A_move_to_nowhere_is_ignored()
    {
        var rig = new DeckRig();
        var vm = await EmptyAsync(rig);

        vm.MoveMini(double.NaN, 10);

        Assert.Null(vm.MiniPosition);
        Assert.Null(rig.SavedPreferences?.MiniLeft);
    }
}

public class MiniPlacementTests
{
    private static readonly Size Mini = new(328, 250);
    private static readonly Rect Work = new(0, 0, 2560, 1392);
    private static readonly Rect Screens = new(0, 0, 2560, 1440);

    [Fact]
    public void A_first_mini_sits_in_the_corner_above_the_tray() =>
        Assert.Equal(new Point(2560 - 328 - MiniPlacement.Margin, 1392 - 250 - MiniPlacement.Margin), MiniPlacement.Place(null, Mini, Work, Screens));

    [Fact]
    public void A_saved_place_is_kept() =>
        Assert.Equal(new Point(300, 200), MiniPlacement.Place((300, 200), Mini, Work, Screens));

    [Fact]
    public void A_saved_place_on_a_screen_that_has_gone_comes_back_inside()
    {
        // Left on a monitor to the right that is no longer plugged in.
        var place = MiniPlacement.Place((3000, 1500), Mini, Work, Screens);

        Assert.Equal(new Point(2560 - 328, 1440 - 250), place);
    }

    [Fact]
    public void A_saved_place_on_a_second_screen_is_kept()
    {
        var screens = new Rect(-1920, 0, 4480, 1440);

        Assert.Equal(new Point(-1500, 300), MiniPlacement.Place((-1500, 300), Mini, Work, screens));
    }
}
