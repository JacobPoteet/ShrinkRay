using ShrinkRay.App.Services;
using ShrinkRay.App.ViewModels;

namespace ShrinkRay.App.Tests;

/// <summary>The show queue: shows lined up behind the one on the tape.</summary>
public class QueueTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static DeckRig Library()
    {
        var rig = new DeckRig();
        rig.Add("Bluey", 1, 1);
        rig.Add("Scrubs", 1, 1);
        rig.Add("Scrubs", 1, 2);
        rig.Add("Sintel", 1, 1);
        return rig;
    }

    private static string First(string show) => DeckRig.Episode(show, 1, 1);

    private static ShowRowViewModel Row(MainViewModel vm, string show) => vm.Shows.Single(s => s.FolderName == show);

    private static void Toggle(MainViewModel vm, string show) => Row(vm, show).QueueCommand!.Execute(null);

    private static async Task RunAsync(MainViewModel vm)
    {
        vm.EncodeCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    private static async Task<(DeckRig Rig, MainViewModel Vm)> HoldingAsync(string held, params string[] queued)
    {
        var rig = Library();
        rig.Runner.Hold[held] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        foreach (var show in queued)
        {
            Toggle(vm, show);
        }

        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        return (rig, vm);
    }

    [Fact]
    public async Task Queued_shows_run_in_order_after_the_one_on_the_tape()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Scrubs");
        Toggle(vm, "Sintel");
        Toggle(vm, "Bluey");
        Assert.Equal(["Sintel", "Bluey"], vm.QueuedShows);
        Assert.Equal("1", Row(vm, "Sintel").Mark);
        Assert.Equal("2", Row(vm, "Bluey").Mark);
        Assert.StartsWith("QUEUE 2 SHOWS", vm.QueueLine);
        Assert.Contains("queued number 2", Row(vm, "Bluey").AutomationName);

        await RunAsync(vm);

        Assert.Equal(
            [First("Scrubs"), DeckRig.Episode("Scrubs", 1, 2), First("Sintel"), First("Bluey")],
            rig.Runner.Ran);
        Assert.Empty(vm.QueuedShows);
        Assert.Equal("", vm.QueueLine);
        Assert.Equal("Bluey", vm.LoadedName);
        Assert.All(vm.Tracks, t => Assert.Equal(TrackKind.Shrunk, t.Kind));
        Assert.Equal("Bluey", rig.SavedPreferences!.LastShow);
    }

    [Fact]
    public async Task Each_queued_show_encodes_at_its_own_quality()
    {
        var rig = Library();
        rig.Settings.ShowCqOverrides["Sintel"] = 24;
        var vm = await rig.StartedWith("Scrubs");
        Toggle(vm, "Sintel");

        await RunAsync(vm);

        Assert.Equal(28, rig.Runner.RanWith[First("Scrubs")].CqForShow("Scrubs"));
        Assert.Equal(24, rig.Runner.RanWith[First("Sintel")].CqForShow("Sintel"));
        Assert.Equal(24, rig.Ledger.Get(First("Sintel"))!.Cq);
        Assert.Equal(28, rig.Ledger.Get(First("Scrubs"))!.Cq);
    }

    [Fact]
    public async Task A_queued_shows_quality_set_during_the_run_applies_when_its_turn_comes()
    {
        var (rig, vm) = await HoldingAsync(First("Scrubs"), "Sintel");
        vm.SelectedShow = Row(vm, "Sintel");

        vm.CqIndex = MainViewModel.CqDetents.ToList().IndexOf(22);
        rig.Runner.Hold[First("Scrubs")].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Equal(22, rig.Runner.RanWith[First("Sintel")].CqForShow("Sintel"));
        Assert.Equal(28, rig.Runner.RanWith[DeckRig.Episode("Scrubs", 1, 2)].CqForShow("Scrubs"));
    }

    [Fact]
    public async Task A_browsed_show_joins_the_queue_during_a_run_and_the_running_one_cannot()
    {
        var (rig, vm) = await HoldingAsync(First("Scrubs"));
        Assert.False(vm.CanQueueShown);
        Assert.False(Row(vm, "Scrubs").QueueCommand!.CanExecute(null));

        vm.SelectedShow = Row(vm, "Sintel");
        Assert.True(vm.CanQueueShown);
        Assert.Equal("Add to queue", vm.QueueKeyText);
        vm.ToggleQueueCommand.Execute(null);

        Assert.Equal(["Sintel"], vm.QueuedShows);
        Assert.Equal("Remove from queue", vm.QueueKeyText);
        Assert.Contains("1 show queued", vm.ShowLeftDetail);
        rig.Runner.Hold[First("Scrubs")].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
        Assert.Contains(First("Sintel"), rig.Runner.Ran);
    }

    [Fact]
    public async Task A_show_taken_out_of_the_queue_does_not_run()
    {
        var (rig, vm) = await HoldingAsync(First("Scrubs"), "Sintel", "Bluey");

        Toggle(vm, "Sintel");
        rig.Runner.Hold[First("Scrubs")].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.DoesNotContain(First("Sintel"), rig.Runner.Ran);
        Assert.Contains(First("Bluey"), rig.Runner.Ran);
    }

    [Fact]
    public async Task A_queued_show_is_read_when_its_turn_comes_so_new_episodes_run()
    {
        var (rig, vm) = await HoldingAsync(First("Scrubs"), "Sintel");
        var late = rig.Add("Sintel", 1, 2);

        rig.Runner.Hold[First("Scrubs")].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Contains(late, rig.Runner.Ran);
    }

    [Fact]
    public async Task The_tape_switches_when_a_queued_show_starts_and_a_browse_of_another_show_carries_on()
    {
        var rig = Library();
        rig.Runner.Hold[First("Scrubs")] = new TaskCompletionSource();
        rig.Runner.Hold[First("Sintel")] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        Toggle(vm, "Sintel");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        vm.SelectedShow = Row(vm, "Bluey");

        rig.Runner.Hold[First("Scrubs")].SetResult();
        await rig.Runner.WaitUntilHeldAsync();

        rig.OnUiThread(() =>
        {
            Assert.Equal("Sintel", vm.LoadedName);
            Assert.Equal("SINTEL", vm.LabelTitle);
            Assert.True(vm.IsBrowsing);
            Assert.Equal("Bluey", vm.TracksHeading);
            Assert.Equal("Back to Sintel", vm.BackText);
            vm.BackToRunCommand.Execute(null);
            Assert.Equal("S01E01", vm.Tracks.Single().Code);
            Assert.Equal(TrackKind.Encoding, vm.Tracks[0].Kind);
        });
        rig.Runner.Hold[First("Sintel")].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Stop_after_this_episode_leaves_the_queue_waiting()
    {
        var (rig, vm) = await HoldingAsync(First("Scrubs"), "Sintel");

        vm.StopCommand.Execute(null);
        vm.StopPrimaryCommand.Execute(null);
        rig.Runner.Hold[First("Scrubs")].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Single(rig.Runner.Ran);
        Assert.Equal(["Sintel"], vm.QueuedShows);
    }

    [Fact]
    public async Task A_stop_while_the_next_show_is_read_keeps_it_queued_and_the_tape_in_place()
    {
        var (rig, vm) = await HoldingAsync(DeckRig.Episode("Scrubs", 1, 2), "Sintel");
        rig.Disk.OnEnumerate = path =>
        {
            if (path.EndsWith("Sintel", StringComparison.OrdinalIgnoreCase))
            {
                rig.Disk.OnEnumerate = null;
                rig.OnUiThread(() => vm.StopCommand.Execute(null));
            }
        };

        rig.Runner.Hold[DeckRig.Episode("Scrubs", 1, 2)].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.DoesNotContain(First("Sintel"), rig.Runner.Ran);
        Assert.Equal(["Sintel"], vm.QueuedShows);
        Assert.Equal("Scrubs", vm.LoadedName);
    }

    [Fact]
    public async Task A_library_that_drops_before_a_queued_show_stops_the_run_and_keeps_the_queue()
    {
        var (rig, vm) = await HoldingAsync(DeckRig.Episode("Scrubs", 1, 2), "Sintel", "Bluey");
        rig.Disk.Offline = true;

        rig.Runner.Hold[DeckRig.Episode("Scrubs", 1, 2)].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Equal(["Sintel", "Bluey"], vm.QueuedShows);
        Assert.DoesNotContain(First("Sintel"), rig.Runner.Ran);
        Assert.StartsWith("The library could not be reached when Sintel's turn came", vm.Notice);
    }

    [Fact]
    public async Task A_queued_show_the_scan_has_not_reached_yet_is_still_read()
    {
        var rig = Library();
        var vm = rig.Create(new DeckPreferences { LastShow = "Scrubs", Queue = ["Sintel"] });
        var scanned = new TaskCompletionSource();
        var release = new ManualResetEventSlim();
        rig.Disk.OnEnumerate = path =>
        {
            // The scan reads the loaded show first, then stalls on the next one.
            if (path.EndsWith("Bluey", StringComparison.OrdinalIgnoreCase))
            {
                scanned.TrySetResult();
                release.Wait(Timeout);
            }
        };
        var start = vm.StartAsync();
        await scanned.Task.WaitAsync(Timeout);

        vm.EncodeCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);
        release.Set();
        await start.WaitAsync(Timeout);

        Assert.Contains(First("Sintel"), rig.Runner.Ran);
    }

    [Fact]
    public async Task The_queue_is_saved_and_waits_for_encode_after_a_restart()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Scrubs");
        Toggle(vm, "Sintel");
        var saved = rig.SavedPreferences!;
        Assert.Equal(["Sintel"], saved.Queue);

        var again = rig.Create(saved);
        await again.StartAsync();

        Assert.Equal(["Sintel"], again.QueuedShows);
        Assert.False(again.IsEncoding);
        Assert.Empty(rig.Runner.Ran);
    }

    [Fact]
    public async Task A_scan_drops_queued_shows_that_have_nothing_left_to_do()
    {
        var rig = Library();
        var vm = rig.Create(new DeckPreferences { LastShow = "Scrubs", Queue = ["Gone", "Sintel"] });

        await vm.StartAsync();

        Assert.Equal(["Sintel"], vm.QueuedShows);
    }

    [Fact]
    public async Task A_library_that_lists_nothing_keeps_the_queue()
    {
        var rig = new DeckRig();
        var vm = rig.Create(new DeckPreferences { Queue = ["Sintel"] });

        await vm.StartAsync();

        Assert.Equal(["Sintel"], vm.QueuedShows);
    }

    [Fact]
    public async Task Encode_starts_with_the_queue_when_the_tape_has_nothing_left()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Scrubs");
        await RunAsync(vm);
        Toggle(vm, "Sintel");

        await RunAsync(vm);

        Assert.Equal(First("Sintel"), rig.Runner.Ran.Last());
        Assert.Equal("Sintel", vm.LoadedName);
        Assert.Empty(vm.QueuedShows);
    }

    [Fact]
    public async Task A_queued_show_loaded_on_the_tape_runs_first_and_leaves_the_queue()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Scrubs");
        Toggle(vm, "Sintel");
        vm.SelectedShow = Row(vm, "Sintel");
        Assert.Equal("*1", Row(vm, "Sintel").Mark);

        await RunAsync(vm);

        Assert.Equal([First("Sintel")], rig.Runner.Ran);
        Assert.Empty(vm.QueuedShows);
    }
}
