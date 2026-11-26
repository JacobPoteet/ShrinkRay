using ShrinkRay.App.Services;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Processes;

namespace ShrinkRay.App.Tests;

/// <summary>What the log keeps about a run.</summary>
public class LoggingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_run_logs_its_show_each_episode_and_its_summary()
    {
        var rig = new DeckRig();
        var first = rig.Add("Scrubs", 1, 1);
        rig.Runner.Results[first] = new PipelineResult(PipelineOutcome.Failed, "HandBrakeCLI exited with code 3");
        var vm = await rig.StartedWith("Scrubs");

        vm.EncodeCommand.Execute(null);
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.Contains("Run: Scrubs, 1 to do, CQ 28, replacing originals", rig.Logged);
        Assert.Contains($"Failed: {first}\nHandBrakeCLI exited with code 3", rig.Logged);
        Assert.Contains("Run ended: 1 failed.", rig.Logged);
    }

    [Theory]
    [InlineData("HandBrakeCLI.exe", 0, "chatter", false)]
    [InlineData("HandBrakeCLI.exe", 3, "broke", true)]
    [InlineData("ffmpeg.exe", 0, "corrupt frame", true)]
    [InlineData("ffprobe.exe", 0, "", false)]
    public async Task A_tool_is_logged_when_it_fails_or_ffmpeg_complains(string tool, int exit, string error, bool logged)
    {
        var lines = new List<string>();
        var result = new ProcessResult(exit, "", error.Length > 0 ? [error] : []);
        var runner = new LoggingProcessRunner(new Fixed(result), lines.Add);

        var returned = await runner.RunAsync(new ProcessSpec(tool, ["-i", "x.mkv"]), default);

        Assert.Same(result, returned);
        Assert.Equal(logged, lines.Count == 1);
        Assert.All(lines, l => Assert.Contains("-i x.mkv", l));
    }

    private sealed class Fixed(ProcessResult result) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
