using ShrinkRay.Core.Library;
using Xunit.Abstractions;

namespace ShrinkRay.App.Tests;

public class ShowLeftTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>A show with one long episode first and five short ones behind it: about 70 minutes at 100 fps.</summary>
    private static async Task<(DeckRig Rig, ShrinkRay.App.ViewModels.MainViewModel Vm, string First)> RunningAsync()
    {
        var rig = new DeckRig();
        var first = rig.Add("Longshow", 1, 1);
        rig.Probe.Results[first] = rig.Probe.Results[first] with { DurationSeconds = 10800 };
        for (var i = 2; i <= 6; i++)
        {
            rig.Add("Longshow", 1, i);
        }

        rig.Runner.Hold[first] = new TaskCompletionSource();
        var vm = await rig.StartedWith("Longshow");
        vm.EncodeCommand.Execute(null);
        await rig.Runner.WaitUntilHeldAsync();
        return (rig, vm, first);
    }

    [Fact]
    public async Task Jittery_progress_only_steps_the_counter_down_on_minute_and_five_minute_marks()
    {
        var (rig, vm, first) = await RunningAsync();
        const double trueFps = 100;
        const double firstFrames = 25 * 10800;
        var rng = new Random(5);
        var seen = new List<(double Minute, string Digits)>();
        var start = rig.Now;

        // Twenty-five simulated minutes: a tick every half second, a progress report every two seconds,
        // each with speed readings 30% either side of the true speed.
        for (var half = 0; half < 25 * 120; half++)
        {
            rig.Now = start.AddMilliseconds(half * 500);
            if (half % 4 == 0)
            {
                var done = trueFps * (half / 2.0) / firstFrames * 100;
                rig.Runner.Report(done, trueFps * (1 + ((rng.NextDouble() - 0.5) * 0.6)));
            }

            vm.Tick();
            if (seen.Count == 0 || seen[^1].Digits != vm.ShowLeftDigits)
            {
                seen.Add((half / 120.0, vm.ShowLeftDigits));
            }
        }

        output.WriteLine("minute  show left");
        foreach (var (minute, digits) in seen)
        {
            output.WriteLine($"{minute,6:0.0}  {digits.Trim()}");
        }

        // After the first minute settles the estimate, every change is downward, and above an hour in fives.
        var settled = seen.Where(s => s.Minute > 1.5).Select(s => ToMinutes(s.Digits)).ToList();
        for (var i = 1; i < settled.Count; i++)
        {
            Assert.True(settled[i] < settled[i - 1], $"{settled[i - 1]} then {settled[i]}");
            Assert.True(settled[i] % 5 == 0 || settled[i] <= 60, $"{settled[i]} is not a mark");
        }

        // Over 25 minutes the counter drops by about that much.
        Assert.InRange(settled[0] - settled[^1], 20, 30);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    [Fact]
    public async Task The_counter_holds_while_paused_and_carries_on_from_the_same_value()
    {
        var (rig, vm, first) = await RunningAsync();
        rig.Runner.Report(10, 100);
        vm.Tick();
        var before = vm.ShowLeftDigits;

        vm.PauseCommand.Execute(null);
        rig.Now = rig.Now.AddMinutes(30);
        vm.Tick();
        Assert.Equal(before, vm.ShowLeftDigits);

        vm.PauseCommand.Execute(null);
        vm.Tick();
        Assert.Equal(before, vm.ShowLeftDigits);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Finished_episodes_drop_out_of_the_counter_and_it_reads_zero_when_the_run_ends()
    {
        var (rig, vm, first) = await RunningAsync();
        rig.Runner.Report(50, 100);
        vm.Tick();
        var during = ToMinutes(vm.ShowLeftDigits);

        rig.Runner.Hold[first].SetResult();
        await vm.CurrentRun.WaitAsync(Timeout);

        Assert.True(during > 0);
        Assert.Equal("0:00", vm.ShowLeftDigits.Trim());
    }

    private static int ToMinutes(string digits)
    {
        var parts = digits.Trim().Split(':');
        return (int.Parse(parts[0]) * 60) + int.Parse(parts[1]);
    }
}
