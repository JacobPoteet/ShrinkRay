using ShrinkRay.App.Tray;
using ShrinkRay.App.ViewModels;
using ShrinkRay.Core.Pipeline;

namespace ShrinkRay.App.Tests;

/// <summary>A run that stops itself, and what a run says at its end.</summary>
public class RunReportTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static DeckRig Library(int episodes = 5)
    {
        var rig = new DeckRig();
        for (var i = 1; i <= episodes; i++)
        {
            rig.Add("Scrubs", 1, i);
        }

        return rig;
    }

    private static string Episode(int n) => DeckRig.Episode("Scrubs", 1, n);

    private static async Task<(MainViewModel Vm, List<RunReport> Reports)> RunAsync(DeckRig rig)
    {
        var vm = await rig.StartedWith("Scrubs");
        var tray = new TrayModel(vm);
        var reports = new List<RunReport>();
        tray.Notified += (_, report) => reports.Add(report);
        vm.EncodeCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);
        return (vm, reports);
    }

    [Fact]
    public async Task A_finished_run_sums_up_on_the_monitor_and_tells_the_tray()
    {
        var rig = Library(3);
        rig.Runner.Results[Episode(2)] = new PipelineResult(PipelineOutcome.Failed, "HandBrakeCLI exited with code 3");

        var (vm, reports) = await RunAsync(rig);

        // Two episodes of 1 GB at 36% each give back 0.64 GB apiece.
        Assert.Equal("Run done: 2 shrunk (1.28 GB saved), 1 failed.", vm.Notice);
        var report = Assert.Single(reports);
        Assert.Equal("ShrinkRay finished the run", report.Title);
        Assert.Equal("2 shrunk (1.28 GB saved), 1 failed.", report.Text);
        Assert.True(report.NeedsAttention);
    }

    [Fact]
    public async Task Failures_in_a_row_stop_the_run_and_say_why()
    {
        var rig = Library();
        foreach (var i in new[] { 1, 2, 3 })
        {
            rig.Runner.Results[Episode(i)] = new PipelineResult(PipelineOutcome.Failed, "The share is gone.");
        }

        var (vm, reports) = await RunAsync(rig);

        Assert.Equal(3, rig.Runner.Ran.Count);
        Assert.False(vm.IsEncoding);
        Assert.Equal("3 episodes in a row failed, so the run stopped. The last one: The share is gone. So far: 3 failed.", vm.Notice);
        var report = Assert.Single(reports);
        Assert.Equal("ShrinkRay stopped the run", report.Title);
        Assert.True(report.NeedsAttention);
    }

    [Fact]
    public async Task A_full_scratch_disk_stops_the_run_before_the_episode_starts()
    {
        var rig = Library();
        rig.FreeBytes = DeckRig.Gigabyte * 3 / 2;

        var (vm, reports) = await RunAsync(rig);

        Assert.Empty(rig.Runner.Ran);
        Assert.StartsWith("Scratch has 1.50 GB free and S01E01 needs about 2.00 GB, so the run stopped.", vm.Notice);
        Assert.All(vm.Tracks, t => Assert.Equal(TrackKind.Queued, t.Kind));
        Assert.Equal("ShrinkRay stopped the run", Assert.Single(reports).Title);
    }

    [Fact]
    public async Task A_run_stopped_from_the_deck_sums_up_without_a_notification()
    {
        var rig = Library(3);
        rig.Runner.Hold[Episode(2)] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Scrubs");
        using var tray = new TrayModel(vm);
        var reports = new List<RunReport>();
        tray.Notified += (_, report) => reports.Add(report);
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();

        vm.StopCommand.Execute(null);
        vm.StopNowCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Equal("Run stopped: 1 shrunk (0.64 GB saved).", vm.Notice);
        Assert.Empty(reports);
    }
}
