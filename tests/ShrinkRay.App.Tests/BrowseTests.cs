using ShrinkRay.Core.Pipeline;
using ShrinkRay.App.Tray;
using ShrinkRay.App.ViewModels;
using ShrinkRay.Core.Library;
using ShrinkRay.Core.Swap;

namespace ShrinkRay.App.Tests;

/// <summary>Browsing another show in the track list while one encodes.</summary>
public class BrowseTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Scrubs encodes. Sintel has one episode in each state the journal and ledger can leave.</summary>
    private static DeckRig Library()
    {
        var rig = new DeckRig();
        rig.Add("Scrubs", 1, 1);
        rig.Add("Scrubs", 1, 2);
        rig.Add("Scrubs", 2, 1);

        rig.Add("Sintel", 1, 1);
        var shrunk = rig.Add("Sintel", 1, 2, codec: "hevc", size: 360_000_000);
        rig.Ledger.Record(new LedgerEntry(shrunk, DeckRig.Gigabyte, 360_000_000, 28, DateTimeOffset.UnixEpoch));
        var failed = rig.Add("Sintel", 1, 3);
        rig.Journal.Record(failed, SwapState.Queued);
        rig.Journal.Record(failed, SwapState.Failed, "The encode crashed.");
        var kept = rig.Add("Sintel", 1, 4);
        rig.Journal.Record(kept, SwapState.Queued);
        rig.Journal.Record(kept, SwapState.CopiedToScratch);
        rig.Journal.Record(kept, SwapState.Encoded);
        rig.Journal.Record(kept, SwapState.Verified);
        rig.Journal.Record(kept, SwapState.KeptOriginal, "Saves 9%, below the 15% minimum");
        rig.Add("Sintel", 2, 1);
        return rig;
    }

    private static async Task<(DeckRig Rig, MainViewModel Vm, string First)> EncodingScrubsAsync()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        return (rig, vm, first);
    }

    private static async Task FinishAsync(DeckRig rig, MainViewModel vm, string first)
    {
        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    private static void Browse(MainViewModel vm, string show) =>
        vm.SelectedShow = vm.Shows.Single(s => s.FolderName == show);

    [Fact]
    public async Task Picking_another_show_during_an_encode_lists_it_and_leaves_the_tape_alone()
    {
        var (rig, vm, first) = await EncodingScrubsAsync();

        Browse(vm, "Sintel");

        Assert.True(vm.IsBrowsing);
        Assert.Equal("Sintel", vm.TracksHeading);
        Assert.Equal("Back to Scrubs", vm.BackText);
        Assert.Equal(["S01E01", "S01E02", "S01E03", "S01E04"], vm.Tracks.Select(t => t.Code));
        Assert.Equal(["S1", "S2"], vm.Seasons.Select(s => s.Label));

        // The tape, the run and the loaded mark stay with Scrubs.
        Assert.Equal("Scrubs", vm.LoadedName);
        Assert.True(vm.Shows.Single(s => s.FolderName == "Scrubs").IsLoaded);
        Assert.False(vm.Shows.Single(s => s.FolderName == "Sintel").IsLoaded);
        Assert.Equal("SCRUBS", vm.LabelTitle);
        Assert.Equal("S01E01", vm.EpisodeDigits);
        Assert.True(vm.IsEncoding);
        await FinishAsync(rig, vm, first);
    }

    [Fact]
    public async Task Browsed_rows_show_their_real_state_from_the_journal_and_the_ledger()
    {
        var (rig, vm, first) = await EncodingScrubsAsync();

        Browse(vm, "Sintel");

        var rows = vm.Tracks.ToDictionary(t => t.Code);
        Assert.Equal(TrackKind.Queued, rows["S01E01"].Kind);
        Assert.Equal("", rows["S01E01"].StatusText);
        Assert.Equal(TrackKind.Shrunk, rows["S01E02"].Kind);
        Assert.Contains("64", rows["S01E02"].StatusText);
        Assert.Equal(TrackKind.Failed, rows["S01E03"].Kind);
        Assert.Equal("The encode crashed.", rows["S01E03"].Detail);
        Assert.Equal(TrackKind.Kept, rows["S01E04"].Kind);
        Assert.DoesNotContain(vm.Tracks, t => t.Kind == TrackKind.Encoding);
        await FinishAsync(rig, vm, first);
    }

    [Fact]
    public async Task Progress_updates_never_touch_the_browsed_list()
    {
        var (rig, vm, first) = await EncodingScrubsAsync();
        Browse(vm, "Sintel");
        var rows = vm.Tracks.ToList();
        var statuses = rows.Select(t => (t.Kind, t.StatusText, t.Progress)).ToList();

        rig.Runner.Report(73);

        Assert.Equal(ScriptedRunner.Percent(ScriptedRunner.Timing.Fraction(EpisodePhase.Encoding, 0.73)), vm.PercentDigits);
        Assert.Equal(rows, vm.Tracks.ToList());
        Assert.Equal(statuses, vm.Tracks.Select(t => (t.Kind, t.StatusText, t.Progress)).ToList());
        Assert.Equal("Sintel", vm.TracksHeading);

        // The loaded show's row took the progress, out of sight.
        BackToScrubs(vm);
        Assert.Equal(TrackKind.Encoding, vm.Tracks[0].Kind);
        await FinishAsync(rig, vm, first);
    }

    [Fact]
    public async Task The_transport_keys_still_act_on_the_run_while_browsing()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        var second = DeckRig.Episode("Scrubs", 1, 2);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        rig.Runner.Hold[second] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        Browse(vm, "Sintel");

        vm.PauseCommand.Execute(null);
        Assert.True(vm.IsPaused);
        Assert.True(rig.Gate.IsPaused);
        vm.EncodeCommand.Execute(null);
        Assert.False(vm.IsPaused);
        Assert.Single(rig.RunSettings);

        // Skip cancels the episode on the tape, not anything in the browsed list.
        vm.SkipCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        Assert.Equal("Sintel", vm.TracksHeading);
        Assert.DoesNotContain(vm.Tracks, t => t.Kind == TrackKind.Skipped);
        vm.BackToRunCommand.Execute(null);
        Assert.Equal(TrackKind.Skipped, vm.Tracks[0].Kind);
        Assert.Equal(TrackKind.Encoding, vm.Tracks[1].Kind);

        rig.Runner.Hold[second].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    [Fact]
    public async Task The_back_key_returns_to_the_encoding_show()
    {
        var (rig, vm, first) = await EncodingScrubsAsync();
        Browse(vm, "Sintel");

        vm.BackToRunCommand.Execute(null);

        Assert.False(vm.IsBrowsing);
        Assert.Equal("Tracks", vm.TracksHeading);
        Assert.Equal("Scrubs", vm.SelectedShow?.FolderName);
        Assert.Equal(["S01E01", "S01E02"], vm.Tracks.Select(t => t.Code));
        Assert.Equal(TrackKind.Encoding, vm.Tracks[0].Kind);
        await FinishAsync(rig, vm, first);
    }

    [Fact]
    public async Task Picking_the_encoding_show_in_the_library_goes_back_too()
    {
        var (rig, vm, first) = await EncodingScrubsAsync();
        Browse(vm, "Sintel");

        Browse(vm, "Scrubs");

        Assert.False(vm.IsBrowsing);
        Assert.Equal(TrackKind.Encoding, vm.Tracks[0].Kind);
        await FinishAsync(rig, vm, first);
    }

    [Fact]
    public async Task Browsing_one_show_after_another_swaps_the_list_without_loading()
    {
        var rig = Library();
        rig.Add("Bluey", 1, 1);
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        Browse(vm, "Sintel");
        Browse(vm, "Bluey");

        Assert.Equal("Bluey", vm.TracksHeading);
        Assert.Equal(["S01E01"], vm.Tracks.Select(t => t.Code));
        Assert.Equal("Scrubs", vm.LoadedName);
        Assert.Equal("Scrubs", rig.SavedPreferences?.LastShow ?? "Scrubs");
        await FinishAsync(rig, vm, first);
    }

    [Fact]
    public async Task When_the_run_ends_while_browsing_the_deck_goes_back_to_normal()
    {
        var (rig, vm, first) = await EncodingScrubsAsync();
        Browse(vm, "Sintel");

        await FinishAsync(rig, vm, first);

        Assert.False(vm.IsEncoding);
        Assert.False(vm.IsBrowsing);
        Assert.Equal("Scrubs", vm.LoadedName);
        Assert.Equal("Scrubs", vm.SelectedShow?.FolderName);
        Assert.Equal("Tracks", vm.TracksHeading);
        Assert.All(vm.Tracks, t => Assert.Equal(TrackKind.Shrunk, t.Kind));

        // Picking a show loads it again, as it does when idle.
        Browse(vm, "Sintel");
        Assert.Equal("Sintel", vm.LoadedName);
        Assert.False(vm.IsBrowsing);
    }

    [Fact]
    public async Task The_quality_row_sets_the_browsed_show_and_never_the_one_encoding()
    {
        var (rig, vm, first) = await EncodingScrubsAsync();
        var saves = rig.SettingsSaves;
        Assert.False(vm.CanEditQuality);

        Browse(vm, "Sintel");

        // Browsing alone saves nothing.
        Assert.True(vm.CanEditQuality);
        Assert.StartsWith("Sintel", vm.CqCaption);
        Assert.Equal(saves, rig.SettingsSaves);

        vm.CqIndex = MainViewModel.CqDetents.ToList().IndexOf(24);
        Assert.Equal(24, Assert.Single(rig.Settings.ShowCqOverrides, o => o.Key == "Sintel").Value);

        BackToScrubs(vm);
        Assert.False(vm.CanEditQuality);
        Assert.Equal(28, vm.Cq);
        Assert.StartsWith("Scrubs", vm.CqCaption);
        vm.CqIndex = 0;
        Assert.DoesNotContain("Scrubs", rig.Settings.ShowCqOverrides.Keys);
        await FinishAsync(rig, vm, first);
    }

    [Fact]
    public async Task The_footer_and_the_tray_read_the_run_and_ignore_browsing()
    {
        var (rig, vm, first) = await EncodingScrubsAsync();
        using var tray = new TrayModel(vm);
        var changes = 0;
        tray.Changed += (_, _) => changes++;
        var state = tray.State;
        var tooltip = tray.Tooltip;
        var caption = vm.SizeCaption;
        var saved = vm.SavedDigits;

        Browse(vm, "Sintel");
        Browse(vm, "Scrubs");
        Browse(vm, "Sintel");

        Assert.Equal(0, changes);
        Assert.Equal(state, tray.State);
        Assert.Equal(tooltip, tray.Tooltip);
        Assert.Equal(caption, vm.SizeCaption);
        Assert.Equal(saved, vm.SavedDigits);
        Assert.Equal("", vm.LastFailureText);
        await FinishAsync(rig, vm, first);
    }

    private static void BackToScrubs(MainViewModel vm) => vm.BackToRunCommand.Execute(null);
}
