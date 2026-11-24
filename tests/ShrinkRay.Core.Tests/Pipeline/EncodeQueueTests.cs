using System.Collections.Concurrent;
using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Processes;

namespace ShrinkRay.Core.Tests.Pipeline;

public class EncodeQueueTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>An episode runner the test finishes by hand. It honours cancellation like the pipeline.</summary>
    internal sealed class ControlledRunner : IEpisodeRunner
    {
        private readonly SemaphoreSlim _started = new(0);

        public ConcurrentDictionary<string, TaskCompletionSource<PipelineResult>> Pending { get; } = new();

        public ConcurrentQueue<string> Started { get; } = new();

        public Exception? Throw { get; set; }

        public async Task<PipelineResult> RunAsync(string sourcePath, IProgress<EncodeProgress>? progress, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<PipelineResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending[sourcePath] = tcs;
            Started.Enqueue(sourcePath);
            progress?.Report(new EncodeProgress(1, 1, 50, 165, 160, null));
            _started.Release();
            if (Throw is { } error)
            {
                throw error;
            }

            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                return await tcs.Task;
            }
        }

        public async Task WaitForStartAsync()
        {
            Assert.True(await _started.WaitAsync(Timeout), "No episode started in time.");
        }

        public void Finish(string path, PipelineOutcome outcome = PipelineOutcome.Replaced) =>
            Pending[path].SetResult(new PipelineResult(outcome, outcome.ToString()));
    }

    internal sealed class RecordingGate : IProcessGate
    {
        public List<string> Calls { get; } = [];

        public bool IsPaused { get; private set; }

        public void Pause()
        {
            IsPaused = true;
            Calls.Add("pause");
        }

        public void Resume()
        {
            IsPaused = false;
            Calls.Add("resume");
        }
    }

    private sealed class Rig
    {
        public ControlledRunner Runner { get; } = new();
        public RecordingGate Gate { get; } = new();
        public ConcurrentQueue<EpisodeFinished> Finished { get; } = new();
        public ConcurrentQueue<QueueState> States { get; } = new();
        public ConcurrentQueue<(string, EncodeProgress)> Progress { get; } = new();
        public bool MayStart { get; set; } = true;
        public int Delays { get; set; }
        public EncodeQueue Queue { get; }

        public Rig()
        {
            Queue = new EncodeQueue(Runner, Gate, () => MayStart, (_, ct) =>
            {
                Delays++;
                MayStart = true;
                return Task.Delay(1, ct);
            });
            Queue.EpisodeFinished += Finished.Enqueue;
            Queue.StateChanged += States.Enqueue;
            Queue.Progress += (path, p) => Progress.Enqueue((path, p));
        }
    }

    [Fact]
    public async Task Runs_episodes_one_at_a_time_in_order()
    {
        var rig = new Rig();
        var run = rig.Queue.RunAsync(["a", "b"]);

        await rig.Runner.WaitForStartAsync();
        Assert.Equal(["a"], rig.Runner.Started);
        Assert.Equal("a", rig.Queue.Current);
        rig.Runner.Finish("a");

        await rig.Runner.WaitForStartAsync();
        rig.Runner.Finish("b", PipelineOutcome.KeptOriginal);
        await run.WaitAsync(Timeout);

        Assert.Equal(["a", "b"], rig.Runner.Started);
        Assert.Equal([PipelineOutcome.Replaced, PipelineOutcome.KeptOriginal], rig.Finished.Select(f => f.Result!.Outcome));
        Assert.All(rig.Finished, f => Assert.Equal(EpisodeEnd.Finished, f.End));
        Assert.Equal(QueueState.Idle, rig.Queue.State);
        Assert.False(rig.Queue.IsRunning);
        Assert.Contains(rig.Progress, p => p.Item1 == "a" && p.Item2.Percent == 50);
    }

    [Fact]
    public async Task Skip_abandons_the_current_episode_and_moves_on()
    {
        var rig = new Rig();
        var run = rig.Queue.RunAsync(["a", "b"]);
        await rig.Runner.WaitForStartAsync();

        rig.Queue.Skip();
        await rig.Runner.WaitForStartAsync();
        rig.Runner.Finish("b");
        await run.WaitAsync(Timeout);

        Assert.Equal([("a", EpisodeEnd.Skipped), ("b", EpisodeEnd.Finished)], rig.Finished.Select(f => (f.Path, f.End)));
    }

    [Fact]
    public async Task Stop_abandons_the_current_episode_and_ends_the_run()
    {
        var rig = new Rig();
        var run = rig.Queue.RunAsync(["a", "b", "c"]);
        await rig.Runner.WaitForStartAsync();

        rig.Queue.Stop();
        await run.WaitAsync(Timeout);

        Assert.Equal(["a"], rig.Runner.Started);
        var only = Assert.Single(rig.Finished);
        Assert.Equal(EpisodeEnd.Stopped, only.End);
        Assert.Null(only.Result);
    }

    [Fact]
    public async Task Pause_suspends_the_tools_and_resume_carries_on()
    {
        var rig = new Rig();
        var run = rig.Queue.RunAsync(["a"]);
        await rig.Runner.WaitForStartAsync();

        rig.Queue.Pause();
        Assert.Equal(QueueState.Paused, rig.Queue.State);
        Assert.True(rig.Gate.IsPaused);

        rig.Queue.Resume();
        Assert.Equal(QueueState.Running, rig.Queue.State);
        rig.Runner.Finish("a");
        await run.WaitAsync(Timeout);

        Assert.Equal(["pause", "resume", "resume"], rig.Gate.Calls);
        Assert.Equal(EpisodeEnd.Finished, Assert.Single(rig.Finished).End);
    }

    [Fact]
    public async Task A_pause_holds_the_next_episode_until_resume()
    {
        var rig = new Rig();
        var run = rig.Queue.RunAsync(["a", "b"]);
        await rig.Runner.WaitForStartAsync();

        rig.Queue.Pause();
        rig.Runner.Finish("a");
        await Task.Delay(100);
        Assert.Equal(["a"], rig.Runner.Started);
        Assert.Equal(QueueState.Paused, rig.Queue.State);

        rig.Queue.Resume();
        await rig.Runner.WaitForStartAsync();
        rig.Runner.Finish("b");
        await run.WaitAsync(Timeout);

        Assert.Equal(["a", "b"], rig.Runner.Started);
    }

    [Fact]
    public async Task Stop_while_paused_releases_the_gate()
    {
        var rig = new Rig();
        var run = rig.Queue.RunAsync(["a", "b"]);
        await rig.Runner.WaitForStartAsync();
        rig.Queue.Pause();

        rig.Queue.Stop();
        await run.WaitAsync(Timeout);

        Assert.False(rig.Gate.IsPaused);
        Assert.Equal(EpisodeEnd.Stopped, Assert.Single(rig.Finished).End);
    }

    [Fact]
    public async Task Waits_for_the_encode_window_before_starting()
    {
        var rig = new Rig { MayStart = false };
        var run = rig.Queue.RunAsync(["a"]);
        await rig.Runner.WaitForStartAsync();
        rig.Runner.Finish("a");
        await run.WaitAsync(Timeout);

        Assert.Equal(1, rig.Delays);
        Assert.Contains(QueueState.WaitingForWindow, rig.States);
    }

    [Fact]
    public async Task A_runner_error_reports_failure_and_carries_on()
    {
        var rig = new Rig();
        rig.Runner.Throw = new InvalidOperationException("Run recovery first.");
        var run = rig.Queue.RunAsync(["a", "b"]);
        await run.WaitAsync(Timeout);

        Assert.Equal(2, rig.Finished.Count);
        Assert.All(rig.Finished, f => Assert.Equal(PipelineOutcome.Failed, f.Result!.Outcome));
        Assert.Equal("Run recovery first.", rig.Finished.First().Result!.Message);
    }

    [Fact]
    public async Task A_second_run_while_running_is_refused()
    {
        var rig = new Rig();
        var run = rig.Queue.RunAsync(["a"]);
        await rig.Runner.WaitForStartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Queue.RunAsync(["b"]));

        rig.Runner.Finish("a");
        await run.WaitAsync(Timeout);
    }

    [Fact]
    public void Transport_keys_do_nothing_when_idle()
    {
        var rig = new Rig();

        rig.Queue.Pause();
        rig.Queue.Skip();
        rig.Queue.Stop();

        Assert.Equal(QueueState.Idle, rig.Queue.State);
        Assert.Empty(rig.Gate.Calls.Where(c => c == "pause"));
    }
}
