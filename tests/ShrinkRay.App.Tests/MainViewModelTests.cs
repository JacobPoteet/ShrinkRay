using ShrinkRay.App.Services;
using ShrinkRay.App.ViewModels;
using ShrinkRay.Core.Library;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Swap;
using ShrinkRay.Core.Verification;
using ShrinkRay.Theme;
using ShrinkRay.Theme.Controls;

namespace ShrinkRay.App.Tests;

public class MainViewModelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static DeckRig Library()
    {
        var rig = new DeckRig();
        rig.Add("Scrubs", 1, 1);
        rig.Add("Scrubs", 1, 2);
        rig.Add("Scrubs", 2, 1, size: 2 * DeckRig.Gigabyte);
        rig.Add("Bluey", 1, 1, size: DeckRig.Gigabyte / 2);
        rig.Add("Severance", 1, 1, codec: "hevc");
        rig.Add("Community", 1, 1, codec: "hevc");
        rig.Ledger.Record(new LedgerEntry(DeckRig.Episode("Community", 1, 1), 2 * DeckRig.Gigabyte, DeckRig.Gigabyte, 28, DateTimeOffset.UnixEpoch));
        return rig;
    }

    // ---- Startup and scan ----

    [Fact]
    public async Task Startup_runs_recovery_before_the_library_scan()
    {
        var rig = Library();
        var vm = rig.Create();

        await vm.StartAsync();

        Assert.Equal("recover", rig.Log.First());
        Assert.NotEmpty(rig.Disk.Listed);
        Assert.Equal("RECOVERY: ALL CLEAR", vm.RecoveryText);
    }

    [Fact]
    public async Task Encode_stays_off_until_recovery_finishes()
    {
        var rig = Library();
        var gate = new TaskCompletionSource<IReadOnlyList<RecoveryAction>>();
        var vm = new MainViewModel(new DeckServices
        {
            Disk = rig.Disk,
            Probe = rig.Probe,
            Cache = new ProbeCache(),
            Journal = rig.Journal,
            Ledger = rig.Ledger,
            CreateRunner = _ => rig.Runner,
            Recover = (_, _) => gate.Task,
            DirectoryExists = rig.Disk.DirectoryExists,
        }, rig.Settings, new DeckPreferences { LastShow = "Scrubs" });

        var start = vm.StartAsync();
        Assert.Equal("RECOVERY CHECK...", vm.MonitorMessage);
        Assert.Equal("Recovery check", vm.RunLamp.Label);
        Assert.False(vm.EncodeCommand.CanExecute(null));
        Assert.Empty(rig.Disk.Listed);

        gate.SetResult([new RecoveryAction(@"X:\TV\a.mkv", RecoveryKind.RolledBack, "x"), new RecoveryAction(@"X:\TV\b.mkv", RecoveryKind.NeedsReview, "y")]);
        await start.WaitAsync(Timeout);

        Assert.Equal("RECOVERY: 1 FIXED \u00b7 1 TO REVIEW", vm.RecoveryText);
        Assert.True(vm.EncodeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Monitor_lists_candidates_and_hides_hevc_and_shrunk_shows()
    {
        var vm = Library().Create();

        await vm.StartAsync();

        Assert.Equal(["BLUEY", "SCRUBS"], vm.Shows.Select(s => s.Name));
        Assert.Equal("2 TO SHRINK", vm.ShowCountText);
        Assert.Equal("HIDDEN: 1 SHRUNK \u00b7 1 HEVC", vm.HiddenText);
        Assert.Equal("TV", vm.LibraryLabel);
        Assert.Equal("0.5", vm.Shows[0].SizeText);
    }

    [Fact]
    public async Task Cursor_info_sums_the_show_without_printing_gb_in_the_pixel_font()
    {
        var vm = Library().Create();
        await vm.StartAsync();

        vm.SelectedShow = vm.Shows.Single(s => s.FolderName == "Scrubs");

        var lines = vm.CursorInfo.Split('\n');
        Assert.Equal("2 SEASONS \u00b7 3 EPS", lines[0]);
        Assert.StartsWith("H.264 \u00b7 3 TO DO", lines[1]);
        Assert.Equal("SAVE ~2.6 OF 4.0 @ CQ28", lines[2]);
        Assert.DoesNotContain("GB", vm.CursorInfo);
        Assert.Equal(LampState.Active, vm.LoadLampState);
    }

    [Fact]
    public async Task No_library_says_so_on_the_monitor()
    {
        var rig = Library();
        rig.Settings.LibraryRoot = "";
        var vm = rig.Create();

        await vm.StartAsync();

        Assert.Empty(vm.Shows);
        Assert.Equal("NO LIBRARY. PRESS CHOOSE", vm.MonitorMessage);
        Assert.Equal(LampState.Off, vm.LibraryLamp.State);
    }

    [Fact]
    public async Task An_empty_scratch_setting_takes_the_default_folder()
    {
        var rig = Library();
        rig.Settings.ScratchFolder = "";

        await rig.Create().StartAsync();

        Assert.Equal(@"C:\Default\Scratch", rig.Settings.ScratchFolder);
        Assert.True(rig.SettingsSaves > 0);
    }

    // ---- Tape ----

    [Fact]
    public async Task Load_tape_fills_the_deck_seasons_and_track_list()
    {
        var rig = Library();
        var vm = rig.Create();
        await vm.StartAsync();
        Assert.False(vm.IsTapeLoaded);

        vm.SelectedShow = vm.Shows.Single(s => s.FolderName == "Scrubs");
        vm.LoadTapeCommand.Execute(null);

        Assert.True(vm.IsTapeLoaded);
        Assert.Equal("SCRUBS", vm.LabelTitle);
        Assert.Equal("Side A \u00b7 2 seasons \u00b7 3 episodes \u00b7 4.0 GB", vm.LabelSide);
        Assert.Equal("S01E01 \u00b7 Title 1", vm.LabelEpisode);
        Assert.Equal(["S1", "S2"], vm.Seasons.Select(s => s.Label));
        Assert.True(vm.Seasons[0].IsSelected);
        Assert.Equal(["S01E01", "S01E02"], vm.Tracks.Select(t => t.Code));
        Assert.All(vm.Tracks, t => Assert.Equal("QUEUED", t.StatusText));
        Assert.Equal("1.00", vm.Tracks[0].SizeText);
        Assert.Equal("*", vm.Shows.Single(s => s.FolderName == "Scrubs").Mark);
        Assert.Equal("Scrubs", rig.SavedPreferences?.LastShow);
        Assert.Equal(1, vm.SourcePack);
    }

    [Fact]
    public async Task Choosing_a_season_chip_swaps_the_track_list()
    {
        var vm = await Library().StartedWith("Scrubs");

        vm.Seasons[1].IsSelected = true;

        Assert.Equal(2, vm.SelectedSeason);
        Assert.Equal(["S02E01"], vm.Tracks.Select(t => t.Code));
        Assert.False(vm.Seasons[0].IsSelected);
        Assert.Equal("Season 2 \u00b7 1 episode", vm.JCardHeader);
    }

    [Fact]
    public async Task Eject_empties_the_deck()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Scrubs");

        vm.EjectCommand.Execute(null);

        Assert.False(vm.IsTapeLoaded);
        Assert.Empty(vm.Tracks);
        Assert.Empty(vm.Seasons);
        Assert.Null(rig.SavedPreferences?.LastShow);
        Assert.Equal("No tape", vm.RunLamp.Label);
    }

    [Fact]
    public async Task Earlier_results_show_on_the_track_list()
    {
        var rig = Library();
        var kept = DeckRig.Episode("Scrubs", 1, 2);
        rig.Journal.Record(kept, SwapState.Queued);
        rig.Journal.Record(kept, SwapState.CopiedToScratch);
        rig.Journal.Record(kept, SwapState.Encoded);
        rig.Journal.Record(kept, SwapState.Verified);
        rig.Journal.Record(kept, SwapState.KeptOriginal, "Saves 9%, below the 15% minimum");

        var vm = await rig.StartedWith("Scrubs");

        var track = vm.Tracks.Single(t => t.Path == kept);
        Assert.Equal(TrackKind.Kept, track.Kind);
        Assert.Equal("Saves 9%, below the 15% minimum", track.Detail);
    }

    [Fact]
    public async Task An_episode_skipped_or_stopped_last_time_reads_queued_not_failed()
    {
        var rig = Library();
        var stopped = DeckRig.Episode("Scrubs", 1, 2);
        rig.Journal.Record(stopped, SwapState.Queued);
        rig.Journal.Record(stopped, SwapState.CopiedToScratch);
        rig.Journal.Record(stopped, SwapState.Encoded);
        rig.Journal.Record(stopped, SwapState.Cancelled, EpisodePipeline.CancelledReason);

        var vm = await rig.StartedWith("Scrubs");

        var track = vm.Tracks.Single(t => t.Path == stopped);
        Assert.Equal(TrackKind.Queued, track.Kind);
        Assert.Equal("QUEUED", track.StatusText);
        Assert.True(track.IsQueueable);
    }

    // ---- Knob ----

    [Fact]
    public async Task Turning_the_knob_saves_a_cq_override_for_the_loaded_show()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Scrubs");
        Assert.Equal(28, vm.Cq);
        var saves = rig.SettingsSaves;

        vm.CqIndex = MainViewModel.CqDetents.ToList().IndexOf(24);

        Assert.Equal(24, rig.Settings.ShowCqOverrides["scrubs"]);
        Assert.True(rig.SettingsSaves > saves);
        Assert.Equal("Est \u221242% per ep", vm.EstimateText);

        vm.CqIndex = MainViewModel.CqDetents.ToList().IndexOf(28);
        Assert.False(rig.Settings.ShowCqOverrides.ContainsKey("Scrubs"));
    }

    [Fact]
    public async Task Loading_a_show_turns_the_knob_to_its_override()
    {
        var rig = Library();
        rig.Settings.ShowCqOverrides["Scrubs"] = 25;

        var vm = await rig.StartedWith("Scrubs");

        Assert.Equal(26, vm.Cq);
    }

    // ---- Encode ----

    [Fact]
    public async Task Encode_runs_every_queued_episode_and_counts_the_space()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Scrubs");

        vm.EncodeCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);
        vm.Seasons[0].IsSelected = true;

        Assert.Equal([DeckRig.Episode("Scrubs", 1, 1), DeckRig.Episode("Scrubs", 1, 2), DeckRig.Episode("Scrubs", 2, 1)], rig.Runner.Ran);
        Assert.False(vm.IsEncoding);
        Assert.All(vm.Tracks, t => Assert.Equal(TrackKind.Shrunk, t.Kind));
        Assert.Equal("\u221264%", vm.Tracks[0].StatusText);
        Assert.Equal("0.36", vm.Tracks[0].SizeText);
        Assert.Equal("  2.6", vm.SavedDigits);
        Assert.Equal("3 of 3 episodes shrunk", vm.EpisodesDoneText);
        Assert.Equal(1, vm.HandledFraction);
        Assert.Equal(640_000_000, rig.Ledger.Get(DeckRig.Episode("Scrubs", 1, 1))!.SavedBytes);
    }

    [Fact]
    public async Task Encode_hands_the_runner_a_snapshot_of_the_settings()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Scrubs");
        vm.CqIndex = MainViewModel.CqDetents.ToList().IndexOf(24);

        vm.EncodeCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);

        var snapshot = Assert.Single(rig.RunSettings);
        Assert.NotSame(rig.Settings, snapshot);
        Assert.Equal(24, snapshot.CqForShow("Scrubs"));
    }

    [Fact]
    public async Task Kept_and_failed_results_show_on_their_tracks()
    {
        var rig = Library();
        var kept = DeckRig.Episode("Scrubs", 1, 1);
        var failed = DeckRig.Episode("Scrubs", 1, 2);
        rig.Runner.Results[kept] = new PipelineResult(PipelineOutcome.KeptOriginal, "Saves 9%",
            new VerificationResult(VerificationOutcome.KeepOriginal, ["Saves 9%"], 0.09));
        rig.Runner.Results[failed] = new PipelineResult(PipelineOutcome.Failed, "Full decode reported errors");
        var vm = await rig.StartedWith("Scrubs");

        vm.EncodeCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);
        vm.Seasons[0].IsSelected = true;

        Assert.Equal("KEPT 9%", vm.Tracks[0].StatusText);
        Assert.Equal("FAILED", vm.Tracks[1].StatusText);
        Assert.Equal("Full decode reported errors", vm.Tracks[1].Detail);
        Assert.Null(rig.Ledger.Get(kept));
    }

    [Fact]
    public async Task While_encoding_the_deck_shows_the_episode_and_locks_the_tape()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");

        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        Assert.True(vm.IsEncoding);
        Assert.True(vm.IsSpinning);
        Assert.Equal("S01E01", vm.EpisodeDigits);
        Assert.Equal("50.0", vm.PercentDigits);
        Assert.Equal("170", vm.FpsDigits);
        Assert.Equal("11:00", vm.TapeTimeDigits);
        Assert.Equal("/ 22:00", vm.TapeTotalText);
        Assert.Equal(0.5, vm.SourcePack);
        Assert.Equal(LampState.Red, vm.EncodeLamp);
        Assert.Equal("Encoding S01E01", vm.RunLamp.Label);
        Assert.Equal(TrackKind.Encoding, vm.Tracks[0].Kind);
        Assert.Equal("50%", vm.Tracks[0].StatusText);
        Assert.Equal(LampState.Active, vm.Seasons[0].Lamp);
        Assert.False(vm.EjectCommand.CanExecute(null));
        Assert.False(vm.LoadTapeCommand.CanExecute(null));
        Assert.False(vm.IsKnobEnabled);
        Assert.False(vm.DropFolder(DeckRig.Root + @"\Bluey"));

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
        Assert.Equal("------", vm.EpisodeDigits);
        Assert.False(vm.IsSpinning);
    }

    [Fact]
    public async Task Pause_holds_the_tools_and_encode_resumes()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        vm.PauseCommand.Execute(null);
        Assert.True(vm.IsPaused);
        Assert.True(rig.Gate.IsPaused);
        Assert.False(vm.IsSpinning);
        Assert.Equal(LampState.Off, vm.EncodeLamp);
        Assert.Equal("Paused", vm.RunLamp.Label);

        vm.EncodeCommand.Execute(null);
        Assert.False(vm.IsPaused);
        Assert.False(rig.Gate.IsPaused);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Stop_puts_the_episode_back_in_the_queue()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        vm.StopCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Single(rig.Runner.Ran);
        Assert.Equal(TrackKind.Queued, vm.Tracks[0].Kind);
        Assert.False(vm.IsEncoding);
    }

    [Fact]
    public async Task Skip_moves_on_and_marks_the_episode_skipped()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        vm.SkipCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);
        vm.Seasons[0].IsSelected = true;

        Assert.Equal(3, rig.Runner.Ran.Count);
        Assert.Equal("SKIPPED", vm.Tracks[0].StatusText);
        Assert.Equal(TrackKind.Shrunk, vm.Tracks[1].Kind);
    }

    [Fact]
    public async Task Encode_with_nothing_queued_says_so()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);

        vm.EncodeCommand.Execute(null);

        Assert.False(vm.IsEncoding);
        Assert.Equal("Nothing left to encode in this show.", vm.Notice);
        Assert.Equal("NOTHING LEFT TO ENCODE IN THIS SHOW.", vm.MonitorNotice);
    }

    [Fact]
    public async Task Tick_reads_the_bitrate_from_the_growing_output_file()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        var output = Path.Combine(EpisodePipeline.WorkFolder(@"C:\Scratch", first), "encoded.mkv");

        rig.Sizes[output] = 0;
        vm.Tick();
        Assert.Equal(0, vm.BitrateMbps);

        // Half of a 1320-second episode is 660 seconds; 165 MB over that averages 2 Mbps.
        rig.Sizes[output] = 165_000_000;
        vm.Tick();
        Assert.Equal(2.0, vm.BitrateMbps, 2);

        // A burst far past the scale pins the needle at the top rather than off it.
        rig.Sizes[output] = 2_000_000_000;
        vm.Tick();
        Assert.Equal(MainViewModel.MaxMbps, vm.BitrateMbps);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
        Assert.Equal(0, vm.BitrateMbps);
    }

    [Fact]
    public async Task The_run_lamp_and_track_follow_the_reported_phase()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        Assert.Equal("Encoding S01E01", vm.RunLamp.Label);
        Assert.Equal("50%", vm.Tracks[0].StatusText);

        // The phase comes from the report, not from the percent.
        rig.Runner.Report(EpisodePhase.Copying);
        Assert.Equal("Copying S01E01", vm.RunLamp.Label);
        Assert.Equal("COPY", vm.Tracks[0].StatusText);
        Assert.Equal("  0", vm.FpsDigits);

        rig.Runner.Report(42);
        Assert.Equal("Encoding S01E01", vm.RunLamp.Label);
        Assert.Equal("42%", vm.Tracks[0].StatusText);
        Assert.Equal("170", vm.FpsDigits);

        rig.Runner.Report(EpisodePhase.Verifying);
        Assert.Equal("Checking S01E01", vm.RunLamp.Label);
        Assert.Equal("CHECK", vm.Tracks[0].StatusText);
        Assert.Equal("  0", vm.FpsDigits);
        Assert.Equal("99.9", vm.PercentDigits);
        Assert.Equal(0, vm.SourcePack);

        rig.Runner.Report(EpisodePhase.Uploading);
        Assert.Equal("Uploading S01E01", vm.RunLamp.Label);
        Assert.Equal("UPLOAD", vm.Tracks[0].StatusText);

        rig.Runner.Report(EpisodePhase.Swapping);
        Assert.Equal("Swapping S01E01", vm.RunLamp.Label);
        Assert.Equal("SWAP", vm.Tracks[0].StatusText);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
        Assert.Equal("Ready", vm.RunLamp.Label);
    }

    // ---- Closing ----

    [Fact]
    public async Task Closing_while_idle_closes_at_once()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Scrubs");

        Assert.True(vm.RequestClose());
        Assert.False(vm.IsCloseOverlayOpen);
    }

    [Fact]
    public async Task Closing_during_an_encode_asks_and_keep_running_carries_on()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        Assert.False(vm.RequestClose());
        Assert.True(vm.IsCloseConfirmOpen);
        Assert.Equal("Stop + close", vm.CloseKeyLabel);
        Assert.Contains("S01E01", vm.CloseText);

        vm.CancelCloseCommand.Execute(null);

        Assert.False(vm.IsCloseOverlayOpen);
        Assert.True(vm.IsEncoding);
        Assert.False(rig.Runner.Tokens[first].IsCancellationRequested);
        Assert.Equal("Encoding S01E01", vm.RunLamp.Label);

        // A second close asks again.
        Assert.False(vm.RequestClose());
        Assert.True(vm.IsCloseConfirmOpen);
        vm.CancelCloseCommand.Execute(null);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
        Assert.Equal(TrackKind.Shrunk, vm.Tracks[0].Kind);
    }

    [Fact]
    public async Task Confirming_before_the_upload_stops_the_episode_then_closes()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        var ready = 0;
        vm.CloseReady += (_, _) => ready++;
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        rig.Runner.Report(EpisodePhase.Uploading);

        Assert.False(vm.RequestClose());
        vm.ConfirmCloseCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);

        // The Stop path: the episode's token was cancelled, so the real pipeline journals
        // Cancelled and clears scratch, and the track goes back in the queue.
        Assert.True(rig.Runner.Tokens[first].IsCancellationRequested);
        Assert.Single(rig.Runner.Ran);
        Assert.Equal(TrackKind.Queued, vm.Tracks[0].Kind);
        Assert.Equal(1, ready);
        Assert.True(vm.RequestClose());
    }

    [Fact]
    public async Task Confirming_past_the_upload_waits_for_the_swap_then_closes()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        var ready = 0;
        vm.CloseReady += (_, _) => ready++;
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        rig.Runner.Report(EpisodePhase.Swapping);

        Assert.False(vm.RequestClose());
        Assert.True(vm.IsFinishingSwap);
        Assert.Equal("Finish + close", vm.CloseKeyLabel);
        vm.ConfirmCloseCommand.Execute(null);

        // The swap is left to finish: nothing cancelled it, the deck says so, and the window stays.
        Assert.True(vm.IsClosing);
        Assert.False(rig.Runner.Tokens[first].IsCancellationRequested);
        Assert.Equal("Finishing swap S01E01", vm.RunLamp.Label);
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
    public async Task A_swap_that_starts_while_the_card_is_open_is_left_to_finish()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        Assert.False(vm.RequestClose());
        Assert.Equal("Stop + close", vm.CloseKeyLabel);
        rig.Runner.Report(EpisodePhase.Uploading);
        rig.Runner.Report(EpisodePhase.Swapping);
        Assert.Equal("Finish + close", vm.CloseKeyLabel);

        vm.ConfirmCloseCommand.Execute(null);
        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.False(rig.Runner.Tokens[first].IsCancellationRequested);
        Assert.Single(rig.Runner.Ran);
        Assert.True(vm.RequestClose());
    }

    // ---- Encode window ----

    [Fact]
    public async Task Encode_outside_the_window_waits_until_it_opens()
    {
        var rig = Library();
        rig.Settings.EncodeWindowEnabled = true;
        rig.Now = new DateTime(2026, 11, 24, 12, 0, 0);
        var vm = await rig.StartedWith("Scrubs");

        vm.EncodeCommand.Execute(null);
        await WaitForAsync(() => vm.RunLamp.Label == "Waiting for window");
        await Task.Delay(50);
        Assert.Empty(rig.Runner.Ran);

        rig.Now = new DateTime(2026, 11, 24, 23, 0, 0);
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Equal(3, rig.Runner.Ran.Count);
    }

    [Fact]
    public async Task A_window_that_closes_mid_episode_finishes_it_and_holds_the_next()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        rig.Settings.EncodeWindowEnabled = true;
        rig.Now = new DateTime(2026, 11, 24, 6, 50, 0);
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        // 07:00 passes while the first episode encodes. It carries on to its swap.
        rig.Now = new DateTime(2026, 11, 24, 7, 10, 0);
        await Task.Delay(50);
        Assert.Equal(TrackKind.Encoding, vm.Tracks[0].Kind);
        rig.Runner.Hold[first].SetResult();
        await WaitForAsync(() => vm.RunLamp.Label == "Waiting for window");

        Assert.Equal(TrackKind.Shrunk, vm.Tracks[0].Kind);
        Assert.Single(rig.Runner.Ran);

        rig.Now = new DateTime(2026, 11, 24, 23, 0, 0);
        await vm.CurrentRun.WaitAsync(Timeout);
        Assert.Equal(3, rig.Runner.Ran.Count);
    }

    // ---- Settings ----

    [Fact]
    public async Task Replace_originals_saves_and_changes_the_footer_lamps()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Scrubs");
        Assert.Equal(LampState.Ok, vm.OriginalsLamp.State);

        vm.ReplaceOriginals = false;

        Assert.False(rig.Settings.ReplaceOriginals);
        Assert.Equal("Originals kept", vm.OriginalsLamp.Label);
        Assert.Equal(LampState.Off, vm.OriginalsLamp.State);
        Assert.Equal("Encodes stay in scratch", vm.RecycleLamp.Label);
    }

    [Fact]
    public async Task Window_keys_move_the_times_and_the_dial_follows()
    {
        var rig = Library();
        rig.Settings.EncodeWindowEnabled = true;
        var vm = await rig.StartedWith("Scrubs");
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var saves = rig.SettingsSaves;

        vm.WindowStartLaterCommand.Execute(null);
        vm.WindowEndEarlierCommand.Execute(null);
        vm.WindowEndEarlierCommand.Execute(null);

        Assert.Equal(new TimeOnly(23, 30), rig.Settings.EncodeWindowStart);
        Assert.Equal(new TimeOnly(6, 0), rig.Settings.EncodeWindowEnd);
        Assert.Equal("23:30", vm.WindowStartText);
        Assert.Equal("06:00", vm.WindowEndText);
        Assert.Equal(23.5, vm.WindowStartHours);
        Assert.Equal(6, vm.WindowEndHours);
        Assert.Contains(nameof(MainViewModel.WindowStartHours), changed);
        Assert.Contains(nameof(MainViewModel.WindowEndHours), changed);
        Assert.Contains(nameof(MainViewModel.WindowText), changed);
        Assert.Equal(saves + 3, rig.SettingsSaves);

        // 02:10 now sits inside 23:30 to 06:00; move the end before it and the window closes.
        Assert.StartsWith("Active", vm.WindowLamp.Label);
        for (var i = 0; i < 8; i++)
        {
            vm.WindowEndEarlierCommand.Execute(null);
        }

        Assert.Equal("02:00", vm.WindowEndText);
        Assert.StartsWith("Closed", vm.WindowLamp.Label);
        Assert.Equal("Runs overnight", vm.WindowDymo);
    }

    [Fact]
    public async Task A_window_that_starts_where_it_ends_runs_any_time()
    {
        var rig = Library();
        rig.Settings.EncodeWindowEnabled = true;
        rig.Settings.EncodeWindowStart = new TimeOnly(7, 0);
        var vm = await rig.StartedWith("Scrubs");

        Assert.Equal("Runs any time", vm.WindowDymo);
        Assert.StartsWith("Active", vm.WindowLamp.Label);
    }

    [Theory]
    [InlineData("23:00", 1, "23:30")]
    [InlineData("23:30", 1, "00:00")]
    [InlineData("00:00", -1, "23:30")]
    [InlineData("07:00", -1, "06:30")]
    [InlineData("23:10", 1, "23:30")]
    [InlineData("23:10", -1, "23:00")]
    public void Window_times_step_by_half_hours_and_wrap_at_midnight(string from, int direction, string expected)
    {
        var time = TimeOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, MainViewModel.StepTime(time, direction).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Settings_that_shape_a_run_are_locked_while_encoding()
    {
        var rig = Library();
        var first = DeckRig.Episode("Scrubs", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        vm.ReplaceOriginals = false;

        Assert.True(rig.Settings.ReplaceOriginals);
        Assert.False(vm.CanEditSettings);
        Assert.False(vm.ChooseScratchCommand.CanExecute(null));
        Assert.False(vm.WindowStartLaterCommand.CanExecute(null));
        vm.WindowEndLaterCommand.Execute(null);
        Assert.Equal(new TimeOnly(7, 0), rig.Settings.EncodeWindowEnd);
        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Phosphor_and_chassis_apply_and_persist()
    {
        var rig = Library();
        var vm = rig.Create();
        await vm.StartAsync();

        vm.PhosphorGreen = true;
        vm.ChassisGraphite = true;

        Assert.Equal((Phosphor.Green, Chassis.Graphite), rig.Themes.Last());
        Assert.Equal(Phosphor.Green, rig.SavedPreferences?.Phosphor);
        Assert.Equal(Chassis.Graphite, rig.SavedPreferences?.Chassis);
        Assert.True(vm.PhosphorGreen);
        Assert.False(vm.PhosphorAmber);
    }

    [Fact]
    public async Task Choosing_a_scratch_folder_saves_it()
    {
        var rig = Library();
        var vm = rig.Create();
        await vm.StartAsync();
        rig.PickedFolder = @"D:\Scratch";

        vm.ChooseScratchCommand.Execute(null);

        Assert.Equal(@"D:\Scratch", rig.Settings.ScratchFolder);
        Assert.Equal("Scratch D:", vm.ScratchLamp.Label);
    }

    [Fact]
    public async Task Encode_with_no_scratch_folder_opens_the_service_panel()
    {
        var rig = Library();
        var vm = rig.Create(new DeckPreferences { LastShow = "Scrubs" });
        await vm.StartAsync();
        rig.Settings.ScratchFolder = "";

        vm.EncodeCommand.Execute(null);

        Assert.False(vm.IsEncoding);
        Assert.True(vm.IsSettingsOpen);
        Assert.Empty(rig.Runner.Ran);
    }

    // ---- Drag and drop ----

    [Fact]
    public async Task Dropping_a_show_folder_from_the_library_loads_it()
    {
        var vm = Library().Create();
        await vm.StartAsync();

        Assert.True(vm.DropFolder(DeckRig.Root + @"\Bluey"));

        Assert.Equal("Bluey", vm.LoadedName);
    }

    [Fact]
    public async Task Dropping_a_show_from_elsewhere_makes_its_parent_the_library()
    {
        var rig = Library();
        var other = @"Y:\Anime\Frieren\Season 01\Frieren - S01E01 - The Journey's End.mkv";
        rig.Disk.AddFile(other, DeckRig.Gigabyte);
        rig.Probe.Results[other] = rig.Probe.Results[DeckRig.Episode("Scrubs", 1, 1)] with { FileName = other };
        var vm = rig.Create();
        await vm.StartAsync();

        Assert.True(vm.DropFolder(@"Y:\Anime\Frieren\Season 01"));
        await WaitForAsync(() => vm.LoadedName == "Frieren");

        Assert.Equal(@"Y:\Anime", rig.Settings.LibraryRoot);
        Assert.Equal("ANIME", vm.LibraryLabel);
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
}
