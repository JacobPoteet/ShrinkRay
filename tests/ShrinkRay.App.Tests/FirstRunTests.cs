using ShrinkRay.App.ViewModels;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Setup;
using ShrinkRay.Theme.Controls;

namespace ShrinkRay.App.Tests;

/// <summary>First-run setup from the self-test to the card after the trial episode, without a window.</summary>
public class FirstRunTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>A fresh install: no library chosen, every tool and a card present. Bluey is the smallest show.</summary>
    private static DeckRig Fresh()
    {
        var rig = new DeckRig { Gpu = new FakeGpu() };
        rig.Add("Scrubs", 1, 1, size: 2 * DeckRig.Gigabyte);
        rig.Add("Scrubs", 1, 2);
        rig.Add("Bluey", 1, 1, size: DeckRig.Gigabyte / 2);
        rig.Add("Bluey", 1, 2, size: DeckRig.Gigabyte / 2);
        rig.Settings.LibraryRoot = "";
        return rig;
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

    private static async Task<MainViewModel> StartAsync(DeckRig rig)
    {
        // The saved preferences carry the loaded show to the next start, as the app does.
        var vm = rig.Create(rig.SavedPreferences);
        await vm.StartAsync();
        await vm.SetupTask.WaitAsync(Timeout);
        return vm;
    }

    private static async Task<MainViewModel> AtScratchAsync(DeckRig rig)
    {
        var vm = await StartAsync(rig);
        rig.PickedFolder = DeckRig.Root;
        vm.ChooseLibraryCommand.Execute(null);
        await WaitForAsync(() => vm.IsLibraryFound);
        vm.SetupNextCommand.Execute(null);
        Assert.True(vm.IsScratchCard);
        return vm;
    }

    private static async Task<MainViewModel> AtOriginalsAsync(DeckRig rig)
    {
        var vm = await AtScratchAsync(rig);
        vm.SetupNextCommand.Execute(null);
        Assert.True(vm.IsOriginalsCard);
        return vm;
    }

    /// <summary>Through the cards and the tour, ready for the trial episode.</summary>
    private static async Task<MainViewModel> ReadyToEncodeAsync(DeckRig rig, bool replace = true)
    {
        var vm = await AtOriginalsAsync(rig);
        (replace ? vm.ReplaceAfterChecksCommand : vm.TrialRunCommand).Execute(null);
        vm.SkipTourCommand.Execute(null);
        Assert.Equal(FirstRunStep.Off, vm.SetupStep);
        return vm;
    }

    private static async Task EncodeAsync(MainViewModel vm)
    {
        vm.EncodeCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    // ---- Whether setup runs ----

    [Fact]
    public async Task A_start_with_no_library_opens_setup_after_recovery_and_holds_the_deck()
    {
        var rig = Fresh();

        var vm = await StartAsync(rig);

        Assert.Equal("recover", rig.Log.First());
        Assert.True(vm.IsSetupCardOpen);
        Assert.False(vm.EncodeCommand.CanExecute(null));
        Assert.False(vm.OpenSettingsCommand.CanExecute(null));
        Assert.False(vm.RescanCommand.CanExecute(null));
        Assert.Equal("Setup", vm.RunLamp.Label);
        Assert.Equal("", vm.MonitorMessage);
        Assert.False(rig.Settings.FirstRunCompleted);
    }

    [Fact]
    public async Task Settings_with_a_library_and_no_flag_skip_setup()
    {
        var rig = Fresh();
        rig.Settings.LibraryRoot = DeckRig.Root;

        var vm = await StartAsync(rig);

        Assert.Equal(FirstRunStep.Off, vm.SetupStep);
        Assert.Null(rig.Settings.FirstRunCompleted);
        Assert.NotEmpty(vm.Shows);
    }

    [Fact]
    public async Task A_setup_quit_before_the_originals_choice_opens_again()
    {
        var rig = Fresh();
        rig.Settings.LibraryRoot = DeckRig.Root;
        rig.Settings.FirstRunCompleted = false;

        var vm = await StartAsync(rig);

        Assert.True(vm.IsLibraryCard);
        Assert.True(vm.IsLibraryFound);
    }

    [Fact]
    public async Task A_second_start_after_the_cards_goes_to_the_deck_with_the_trial_still_to_run()
    {
        var rig = Fresh();
        await ReadyToEncodeAsync(rig);

        var vm = await StartAsync(rig);

        Assert.Equal(FirstRunStep.Off, vm.SetupStep);
        await EncodeAsync(vm);
        Assert.Single(rig.Runner.Ran);
        Assert.True(vm.IsFirstEpisodeCardOpen);
    }

    [Fact]
    public async Task A_start_after_the_first_episode_card_runs_whole_shows()
    {
        var rig = Fresh();
        var first = await ReadyToEncodeAsync(rig);
        await EncodeAsync(first);
        first.DoneForNowCommand.Execute(null);

        var vm = await StartAsync(rig);

        Assert.Equal(FirstRunStep.Off, vm.SetupStep);
        Assert.True(rig.Settings.FirstRunCompleted);
        vm.SelectedShow = vm.Shows.Single(s => s.FolderName == "Scrubs");
        await EncodeAsync(vm);
        Assert.Equal(3, rig.Runner.Ran.Count);
        Assert.False(vm.IsFirstEpisodeCardOpen);
    }

    // ---- Self-test ----

    [Fact]
    public async Task With_every_tool_found_the_self_test_lights_four_lamps_and_moves_on()
    {
        var vm = await StartAsync(Fresh());

        Assert.True(vm.IsLibraryCard);
        Assert.All(vm.ToolChecks, c => Assert.Equal(LampState.Ok, c.Lamp.State));
        Assert.Equal(["HandBrakeCLI", "ffprobe", "ffmpeg", "NVENC · RTX 2070S"], vm.ToolChecks.Select(c => c.Lamp.Label));
        Assert.True(vm.IsLibraryAsking);
        Assert.False(vm.SetupNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_missing_tool_is_named_with_its_command_and_holds_the_card()
    {
        var rig = Fresh();
        rig.MissingTools.Add("ffmpeg.exe");

        var vm = await StartAsync(rig);

        Assert.True(vm.IsToolsCard);
        Assert.True(vm.HasMissingTools);
        var missing = Assert.Single(vm.ToolChecks, c => c.IsMissing);
        Assert.Equal("ffmpeg missing", missing.Lamp.Label);
        Assert.Equal("winget install --id Gyan.FFmpeg -e", missing.Command);
        Assert.False(vm.SetupNextCommand.CanExecute(null));

        missing.CopyCommand.Execute(null);
        Assert.Equal([missing.Command], rig.Copied);
        Assert.Equal("Copied", missing.CopyLabel);
    }

    [Fact]
    public async Task Check_again_after_installing_moves_on()
    {
        var rig = Fresh();
        rig.MissingTools.Add("HandBrakeCLI.exe");
        var vm = await StartAsync(rig);

        rig.MissingTools.Clear();
        vm.CheckToolsAgainCommand.Execute(null);
        await vm.SetupTask.WaitAsync(Timeout);

        Assert.True(vm.IsLibraryCard);
        Assert.All(vm.ToolChecks, c => Assert.False(c.IsMissing));
    }

    [Fact]
    public async Task Locate_saves_the_chosen_program_and_tests_again()
    {
        var rig = Fresh();
        rig.MissingTools.Add("ffprobe.exe");
        var vm = await StartAsync(rig);

        rig.PickedProgram = @"C:\Tools\ffprobe.exe";
        vm.ToolChecks[1].LocateCommand.Execute(null);
        await vm.SetupTask.WaitAsync(Timeout);

        Assert.Equal(@"C:\Tools\ffprobe.exe", rig.Settings.FfprobePath);
        Assert.True(vm.IsLibraryCard);
    }

    [Fact]
    public async Task Without_an_NVENC_card_setup_stays_on_the_self_test()
    {
        var rig = Fresh();
        rig.Gpu!.Card = null;

        var vm = await StartAsync(rig);

        Assert.True(vm.IsToolsCard);
        var card = vm.ToolChecks[3];
        Assert.True(card.IsMissing);
        Assert.False(card.HasCommand);
        Assert.False(card.CanLocate);
        Assert.NotEqual("", card.Note);
    }

    // ---- Library ----

    [Fact]
    public async Task A_TV_root_shows_what_the_library_gives_back()
    {
        var rig = Fresh();
        var vm = await StartAsync(rig);

        rig.PickedFolder = DeckRig.Root;
        vm.ChooseLibraryCommand.Execute(null);
        await WaitForAsync(() => vm.IsLibraryFound);

        Assert.Equal("2 shows · 4.0 GB · about 2.6 GB to free at CQ 28", vm.PayoffText);
        Assert.Equal("2.6", vm.PayoffDigits);
        Assert.False(vm.ShowsRecycleReminder);
        Assert.True(vm.SetupNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_single_show_folder_works_as_the_library()
    {
        var rig = Fresh();
        var vm = await StartAsync(rig);

        Assert.True(vm.DropFolder($@"{DeckRig.Root}\Bluey"));
        await WaitForAsync(() => vm.IsLibraryFound);

        Assert.StartsWith("1 show · 1.0 GB", vm.PayoffText);
    }

    [Fact]
    public async Task A_folder_with_no_episodes_asks_for_another()
    {
        var rig = Fresh();
        rig.Disk.AddFile(@"X:\Other\Notes\readme.txt", 10);
        var vm = await StartAsync(rig);

        rig.PickedFolder = @"X:\Other";
        vm.ChooseLibraryCommand.Execute(null);
        await WaitForAsync(() => vm.IsLibraryEmpty);

        Assert.Equal("Choose another", vm.LibraryChooseLabel);
        Assert.False(vm.SetupNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_library_on_a_share_asks_about_its_recycle_bin_without_blocking()
    {
        var rig = Fresh();
        rig.NetworkPaths.Add(DeckRig.Root);
        var vm = await StartAsync(rig);

        rig.PickedFolder = DeckRig.Root;
        vm.ChooseLibraryCommand.Execute(null);
        await WaitForAsync(() => vm.IsLibraryFound);

        Assert.True(vm.ShowsRecycleReminder);
        Assert.False(vm.IsRecycleBinChecked);
        Assert.True(vm.SetupNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_drop_is_refused_on_the_other_cards()
    {
        var vm = await AtScratchAsync(Fresh());

        Assert.False(vm.CanDropFolder);
        Assert.False(vm.DropFolder($@"{DeckRig.Root}\Bluey"));
    }

    // ---- Scratch ----

    [Fact]
    public async Task The_default_scratch_folder_takes_one_press()
    {
        var rig = Fresh();
        rig.Settings.ScratchFolder = "";

        var vm = await AtScratchAsync(rig);

        Assert.Equal(@"C:\Default\Scratch", vm.ScratchFolder);
        Assert.Equal("500 GB free on C:", vm.ScratchFreeText);
        Assert.Equal("", vm.ScratchWarning);
        Assert.True(vm.SetupNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Low_space_warns_and_still_lets_the_user_go_on()
    {
        var rig = Fresh();
        rig.FreeBytes = 3 * DeckRig.Gigabyte;

        var vm = await AtScratchAsync(rig);

        Assert.Equal("Low on space. Allow 4.00 GB, twice your largest episode.", vm.ScratchWarning);
        Assert.True(vm.SetupNextCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(@"Z:\Scratch", "That folder is on a share. Choose a local disk.")]
    [InlineData(@"X:\TV\Scratch", "That folder is inside the library. Choose one outside it.")]
    public async Task Scratch_on_a_share_or_in_the_library_is_refused(string folder, string warning)
    {
        var rig = Fresh();
        rig.NetworkPaths.Add(@"Z:\");
        var vm = await AtScratchAsync(rig);

        rig.PickedFolder = folder;
        vm.ChooseScratchCommand.Execute(null);

        Assert.Equal(warning, vm.ScratchWarning);
        Assert.False(vm.SetupNextCommand.CanExecute(null));
    }

    [Fact]
    public async Task Back_walks_from_originals_to_the_library()
    {
        var vm = await AtOriginalsAsync(Fresh());

        vm.SetupBackCommand.Execute(null);
        Assert.True(vm.IsScratchCard);

        vm.SetupBackCommand.Execute(null);
        Assert.True(vm.IsLibraryCard);
        Assert.True(vm.IsLibraryFound);
        Assert.False(vm.SetupBackCommand.CanExecute(null));
    }

    // ---- Originals ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_originals_choice_is_saved_and_starts_the_tour_with_the_smallest_show(bool replace)
    {
        var rig = Fresh();
        var vm = await AtOriginalsAsync(rig);
        Assert.False(vm.SetupNextCommand.CanExecute(null));

        (replace ? vm.ReplaceAfterChecksCommand : vm.TrialRunCommand).Execute(null);

        Assert.Equal(replace, rig.Settings.ReplaceOriginals);
        Assert.True(rig.Settings.FirstEpisodePending);
        Assert.False(rig.Settings.FirstRunCompleted);
        Assert.True(vm.IsTourLibrary);
        Assert.Equal("Bluey", vm.LoadedName);
        Assert.False(vm.EncodeCommand.CanExecute(null));
    }

    // ---- Tour ----

    [Fact]
    public async Task The_tour_walks_three_stops_then_Encode_comes_on()
    {
        var vm = await AtOriginalsAsync(Fresh());
        vm.TrialRunCommand.Execute(null);

        vm.SetupNextCommand.Execute(null);
        Assert.True(vm.IsTourEncode);
        Assert.Equal("Press Encode. Your first run stops after one episode.", vm.TourEncodeText);
        vm.SetupNextCommand.Execute(null);
        Assert.True(vm.IsTourSavings);
        vm.SetupNextCommand.Execute(null);

        Assert.Equal(FirstRunStep.Off, vm.SetupStep);
        Assert.True(vm.EncodeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Escape_skips_the_tour()
    {
        var vm = await AtOriginalsAsync(Fresh());
        vm.TrialRunCommand.Execute(null);

        vm.DismissCardCommand.Execute(null);

        Assert.Equal(FirstRunStep.Off, vm.SetupStep);
    }

    [Fact]
    public async Task Show_the_tour_runs_it_again_from_Setup()
    {
        var rig = Fresh();
        rig.Settings.LibraryRoot = DeckRig.Root;
        var vm = await StartAsync(rig);
        vm.OpenSettingsCommand.Execute(null);

        vm.ShowTourCommand.Execute(null);

        Assert.False(vm.IsSettingsOpen);
        Assert.True(vm.IsTourLibrary);
        Assert.Equal("Press Encode to shrink the show in the deck.", vm.TourEncodeText);
        vm.SkipTourCommand.Execute(null);
        Assert.Null(rig.Settings.FirstRunCompleted);
    }

    [Fact]
    public async Task Run_setup_again_opens_the_cards_and_Cancel_closes_them()
    {
        var rig = Fresh();
        rig.Settings.LibraryRoot = DeckRig.Root;
        var vm = await StartAsync(rig);
        vm.OpenSettingsCommand.Execute(null);

        vm.RunSetupAgainCommand.Execute(null);
        await vm.SetupTask.WaitAsync(Timeout);
        Assert.True(vm.IsLibraryCard);
        Assert.True(vm.CanCancelSetup);

        vm.DismissCardCommand.Execute(null);

        Assert.Equal(FirstRunStep.Off, vm.SetupStep);
        Assert.True(rig.Settings.FirstRunCompleted);
        Assert.False(rig.Settings.FirstEpisodePending);
    }

    [Fact]
    public async Task A_first_setup_cannot_be_cancelled()
    {
        var vm = await StartAsync(Fresh());

        Assert.False(vm.CanCancelSetup);
        Assert.False(vm.SetupCancelCommand.CanExecute(null));
        Assert.False(vm.DismissCardCommand.CanExecute(null));
    }

    // ---- The trial episode ----

    [Fact]
    public async Task The_first_run_arms_the_stop_and_shows_before_and_after()
    {
        var rig = Fresh();
        var vm = await ReadyToEncodeAsync(rig);
        var first = DeckRig.Episode("Bluey", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();

        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        Assert.True(vm.IsStopArmed);
        Assert.Equal("Last episode", vm.RunLamp.Label);
        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Single(rig.Runner.Ran);
        Assert.True(vm.IsFirstEpisodeCardOpen);
        Assert.Equal("S01E01: 0.50 GB", vm.FirstEpisodeBefore);
        Assert.Equal("0.18 GB", vm.FirstEpisodeAfter);
        Assert.Equal("Open it in Plex and check it plays.", vm.FirstEpisodeText);
        Assert.Equal("", vm.FirstEpisodeFile);
        Assert.False(vm.EncodeCommand.CanExecute(null));
        Assert.False(rig.Settings.FirstRunCompleted);
    }

    [Fact]
    public async Task Done_for_now_completes_the_first_run()
    {
        var rig = Fresh();
        var vm = await ReadyToEncodeAsync(rig);
        await EncodeAsync(vm);

        vm.DoneForNowCommand.Execute(null);

        Assert.False(vm.IsFirstEpisodeCardOpen);
        Assert.True(rig.Settings.FirstRunCompleted);
        Assert.False(rig.Settings.FirstEpisodePending);
        Assert.Single(rig.Runner.Ran);
        Assert.True(vm.EncodeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Encode_the_rest_carries_on_through_the_show()
    {
        var rig = Fresh();
        var vm = await ReadyToEncodeAsync(rig);
        await EncodeAsync(vm);

        vm.EncodeRestCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Equal([DeckRig.Episode("Bluey", 1, 1), DeckRig.Episode("Bluey", 1, 2)], rig.Runner.Ran);
        Assert.True(rig.Settings.FirstRunCompleted);
        Assert.False(vm.IsFirstEpisodeCardOpen);
    }

    [Fact]
    public async Task In_trial_mode_the_card_says_where_the_new_file_is()
    {
        var rig = Fresh();
        var vm = await ReadyToEncodeAsync(rig, replace: false);
        const string Kept = @"C:\Scratch\Bluey - S01E01\encoded.mkv";
        rig.Sizes[Kept] = DeckRig.Gigabyte / 4;
        rig.Runner.Results[DeckRig.Episode("Bluey", 1, 1)] =
            new PipelineResult(PipelineOutcome.KeptOriginal, "Replacing originals is off.", KeptEncodePath: Kept);

        await EncodeAsync(vm);

        Assert.False(rig.RunSettings.Single().ReplaceOriginals);
        Assert.True(vm.IsFirstEpisodeCardOpen);
        Assert.Equal("0.25 GB", vm.FirstEpisodeAfter);
        Assert.Equal(Kept, vm.FirstEpisodeFile);
        Assert.Contains("untouched", vm.FirstEpisodeText);
        Assert.True(vm.CanEncodeRest);
    }

    [Theory]
    [InlineData(PipelineOutcome.Failed, "First episode failed")]
    [InlineData(PipelineOutcome.KeptOriginal, "Original kept")]
    public async Task An_episode_with_nothing_new_to_play_leaves_the_trial_to_run_again(PipelineOutcome outcome, string heading)
    {
        var rig = Fresh();
        var vm = await ReadyToEncodeAsync(rig);
        rig.Runner.Results[DeckRig.Episode("Bluey", 1, 1)] = new PipelineResult(outcome, "The reason.");

        await EncodeAsync(vm);

        Assert.Equal(heading, vm.FirstEpisodeHeading);
        Assert.Contains("The reason.", vm.FirstEpisodeText);
        Assert.Equal("OK", vm.FirstEpisodeCloseLabel);
        Assert.False(vm.CanEncodeRest);

        vm.DismissCardCommand.Execute(null);

        Assert.False(vm.IsFirstEpisodeCardOpen);
        Assert.False(rig.Settings.FirstRunCompleted);
        Assert.True(rig.Settings.FirstEpisodePending);
    }

    [Fact]
    public async Task Skipping_the_first_episode_shows_no_card_and_keeps_the_trial()
    {
        var rig = Fresh();
        var vm = await ReadyToEncodeAsync(rig);
        var first = DeckRig.Episode("Bluey", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        vm.SkipCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.False(vm.IsFirstEpisodeCardOpen);
        Assert.True(rig.Settings.FirstEpisodePending);
        Assert.Single(rig.Runner.Ran);
    }

    [Fact]
    public async Task Keep_going_runs_the_whole_show_and_completes_without_a_card()
    {
        var rig = Fresh();
        var vm = await ReadyToEncodeAsync(rig);
        var first = DeckRig.Episode("Bluey", 1, 1);
        rig.Runner.Hold[first] = new TaskCompletionSource();
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        vm.StopCommand.Execute(null);
        Assert.Equal("Keep going", vm.StopPrimaryLabel);
        vm.StopPrimaryCommand.Execute(null);
        Assert.False(vm.IsStopArmed);
        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Equal(2, rig.Runner.Ran.Count);
        Assert.False(vm.IsFirstEpisodeCardOpen);
        Assert.True(rig.Settings.FirstRunCompleted);
    }
}
