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

        public async Task<PipelineResult> RunAsync(string sourcePath, IProgress<EpisodeProgress>? progress, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<PipelineResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending[sourcePath] = tcs;
            Started.Enqueue(sourcePath);
            progress?.Report(new EpisodeProgress(EpisodePhase.Encoding, new EncodeProgress(1, 1, 50, 165, 160, null)));
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

        /// <summary>Runs once, inside the next <see cref="Resume"/>.</summary>
        public Action? OnNextResume { get; set; }

        public void Pause()
        {
            IsPaused = true;
            Calls.Add("pause");
        }

        public void Resume()
        {
            IsPaused = false;
            Calls.Add("resume");
            var hook = OnNextResume;
            OnNextResume = null;
            hook?.Invoke();
        }
    }

    private sealed class Rig
    {
        public ControlledRunner Runner { get; } = new();
        public RecordingGate Gate { get; } = new();
        public ConcurrentQueue<EpisodeFinished> Finished { get; } = new();
        public ConcurrentQueue<QueueState> States { get; } = new();
        public ConcurrentQueue<(string, EpisodeProgress)> Progress { get; } = new();
        public volatile bool MayStart = true;

        /// <summary>False keeps the window shut across polls until the test opens it.</summary>
        public bool OpenOnDelay { get; init; } = true;
        public int Delays { get; set; }
        public EncodeQueue Queue { get; }

        public Rig()
        {
            Queue = new EncodeQueue(Runner, Gate, () => MayStart, (_, ct) =>
            {
                Delays++;
                if (OpenOnDelay)
                {
                    MayStart = true;
                }

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
        Assert.Contains(rig.Progress, p => p.Item1 == "a" && p.Item2.Phase == EpisodePhase.Encoding && p.Item2.Encode!.Percent == 50);
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
    public async Task Stop_while_held_between_episodes_never_starts_the_next()
    {
        var rig = new Rig();
        var run = rig.Queue.RunAsync(["a", "b"]);
        await rig.Runner.WaitForStartAsync();
        rig.Queue.Pause();
        rig.Runner.Finish("a");

        // A slow release of the tools: anything Stop has let go of by now has time to run.
        rig.Gate.OnNextResume = () => SpinWait.SpinUntil(() => rig.Runner.Started.Count > 1, 500);
        rig.Queue.Stop();
        await run.WaitAsync(Timeout);

        Assert.Equal(["a"], rig.Runner.Started);
        Assert.Equal(QueueState.Idle, rig.Queue.State);
        Assert.False(rig.Gate.IsPaused);
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
    public async Task A_window_that_closes_mid_episode_lets_it_finish_and_holds_the_next()
    {
        var rig = new Rig { OpenOnDelay = false };
        var run = rig.Queue.RunAsync(["a", "b"]);
        await rig.Runner.WaitForStartAsync();

        // The window closes while "a" runs. The queue leaves it alone.
        rig.MayStart = false;
        await Task.Delay(50);
        Assert.Empty(rig.Finished);
        rig.Runner.Finish("a");

        var until = DateTime.UtcNow + Timeout;
        while (rig.Queue.State != QueueState.WaitingForWindow && DateTime.UtcNow < until)
        {
            await Task.Delay(5);
        }

        Assert.Equal(QueueState.WaitingForWindow, rig.Queue.State);
        var first = Assert.Single(rig.Finished);
        Assert.Equal(EpisodeEnd.Finished, first.End);
        Assert.Equal(PipelineOutcome.Replaced, first.Result!.Outcome);
        Assert.Equal(["a"], rig.Runner.Started);

        rig.MayStart = true;
        await rig.Runner.WaitForStartAsync();
        rig.Runner.Finish("b");
        await run.WaitAsync(Timeout);
        Assert.Equal(["a", "b"], rig.Runner.Started);
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
    public async Task Stop_after_current_lets_the_episode_finish_and_starts_no_other()
    {
        var rig = new Rig();
        var run = rig.Queue.RunAsync(["a", "b", "c"]);
        await rig.Runner.WaitForStartAsync();

        rig.Queue.StopAfterCurrent();
        Assert.False(run.IsCompleted);
        rig.Runner.Finish("a");
        await run.WaitAsync(Timeout);

        Assert.Equal(["a"], rig.Runner.Started);
        var only = Assert.Single(rig.Finished);
        Assert.Equal(EpisodeEnd.Finished, only.End);
        Assert.Equal(PipelineOutcome.Replaced, only.Result!.Outcome);
        Assert.Equal(QueueState.Idle, rig.Queue.State);
    }

    [Fact]
    public async Task Stop_after_current_releases_a_pause_so_the_episode_can_finish()
    {
        var rig = new Rig();
        var run = rig.Queue.RunAsync(["a", "b"]);
        await rig.Runner.WaitForStartAsync();
        rig.Queue.Pause();

        rig.Queue.StopAfterCurrent();
        Assert.False(rig.Gate.IsPaused);
        rig.Runner.Finish("a");
        await run.WaitAsync(Timeout);

        Assert.Equal(["a"], rig.Runner.Started);
    }

    [Fact]
    public async Task Stop_after_current_between_episodes_stops_at_once()
    {
        var rig = new Rig();
        var waiting = new TaskCompletionSource();
        var queue = new EncodeQueue(rig.Runner, rig.Gate, () => false, async (_, ct) =>
        {
            waiting.TrySetResult();
            await Task.Delay(Timeout, ct);
        });
        var run = queue.RunAsync(["a"]);
        await waiting.Task.WaitAsync(Timeout);

        queue.StopAfterCurrent();
        await run.WaitAsync(Timeout);

        Assert.Empty(rig.Runner.Started);
    }

    [Fact]
    public async Task Continue_after_current_clears_the_request_and_the_run_goes_on()
    {
        var rig = new Rig();
        var run = rig.Queue.RunAsync(["a", "b"]);
        await rig.Runner.WaitForStartAsync();
        rig.Queue.StopAfterCurrent();

        Assert.True(rig.Queue.ContinueAfterCurrent());
        rig.Runner.Finish("a");
        await rig.Runner.WaitForStartAsync();
        rig.Runner.Finish("b");
        await run.WaitAsync(Timeout);

        Assert.Equal(["a", "b"], rig.Runner.Started);
        Assert.Equal([EpisodeEnd.Finished, EpisodeEnd.Finished], rig.Finished.Select(f => f.End));
    }

    [Fact]
    public async Task The_request_can_be_set_and_cleared_again()
    {
        var rig = new Rig();
        var run = rig.Queue.RunAsync(["a", "b"]);
        await rig.Runner.WaitForStartAsync();

        rig.Queue.StopAfterCurrent();
        Assert.True(rig.Queue.ContinueAfterCurrent());
        rig.Queue.StopAfterCurrent();
        rig.Runner.Finish("a");
        await run.WaitAsync(Timeout);

        Assert.Equal(["a"], rig.Runner.Started);
    }

    [Fact]
    public async Task Continue_after_current_does_nothing_without_a_request_or_a_run()
    {
        var rig = new Rig();
        Assert.False(rig.Queue.ContinueAfterCurrent());

        var run = rig.Queue.RunAsync(["a"]);
        await rig.Runner.WaitForStartAsync();
        Assert.False(rig.Queue.ContinueAfterCurrent());

        rig.Runner.Finish("a");
        await run.WaitAsync(Timeout);
        Assert.False(rig.Queue.ContinueAfterCurrent());
    }

    [Fact]
    public async Task Continue_after_current_cannot_undo_a_stop_that_ended_the_run_at_once()
    {
        var rig = new Rig();
        var waiting = new TaskCompletionSource();
        var queue = new EncodeQueue(rig.Runner, rig.Gate, () => false, async (_, ct) =>
        {
            waiting.TrySetResult();
            await Task.Delay(Timeout, ct);
        });
        var run = queue.RunAsync(["a"]);
        await waiting.Task.WaitAsync(Timeout);

        queue.StopAfterCurrent();
        Assert.False(queue.ContinueAfterCurrent());
        await run.WaitAsync(Timeout);

        Assert.Empty(rig.Runner.Started);
    }

    [Fact]
    public async Task Continue_after_current_loses_once_the_finished_episode_committed_the_queue_to_stop()
    {
        var rig = new Rig();
        var run = rig.Queue.RunAsync(["a", "b"]);
        await rig.Runner.WaitForStartAsync();
        rig.Queue.StopAfterCurrent();
        rig.Runner.Finish("a");
        await run.WaitAsync(Timeout);

        Assert.False(rig.Queue.ContinueAfterCurrent());
        Assert.Equal(["a"], rig.Runner.Started);
    }

    [Fact]
    public async Task Clearing_the_request_as_the_episode_finishes_never_loses_or_half_applies_it()
    {
        // The episode ends on one thread while another clears the request. Either the clear wins
        // and the next episode starts, or the queue had already committed to stopping and the
        // clear reports that. It must never say "continuing" and then stop.
        for (var i = 0; i < 300; i++)
        {
            var rig = new Rig();
            var run = rig.Queue.RunAsync(["a", "b"]);
            await rig.Runner.WaitForStartAsync();
            rig.Queue.StopAfterCurrent();

            var gate = new ManualResetEventSlim(false);
            var finish = Task.Run(() =>
            {
                gate.Wait();
                rig.Runner.Finish("a");
            });
            var clear = Task.Run(() =>
            {
                gate.Wait();
                return rig.Queue.ContinueAfterCurrent();
            });
            gate.Set();
            await finish.WaitAsync(Timeout);
            var cleared = await clear.WaitAsync(Timeout);

            if (cleared)
            {
                await rig.Runner.WaitForStartAsync();
                rig.Runner.Finish("b");
                await run.WaitAsync(Timeout);
                Assert.Equal(["a", "b"], rig.Runner.Started);
            }
            else
            {
                await run.WaitAsync(Timeout);
                Assert.Equal(["a"], rig.Runner.Started);
            }
        }
    }

    [Fact]
    public async Task Stop_after_first_ends_the_run_after_one_episode()
    {
        var rig = new Rig();
        rig.Queue.StopAfterFirst = true;

        var run = rig.Queue.RunAsync(["a", "b"]);
        await rig.Runner.WaitForStartAsync();
        rig.Runner.Finish("a");
        await run.WaitAsync(Timeout);

        Assert.Equal(["a"], rig.Runner.Started);
    }

    [Fact]
    public async Task Stop_after_first_can_be_taken_back_during_the_episode()
    {
        var rig = new Rig();
        rig.Queue.StopAfterFirst = true;
        var run = rig.Queue.RunAsync(["a", "b"]);
        await rig.Runner.WaitForStartAsync();

        Assert.True(rig.Queue.ContinueAfterCurrent());
        rig.Runner.Finish("a");
        await rig.Runner.WaitForStartAsync();
        rig.Runner.Finish("b");
        await run.WaitAsync(Timeout);

        Assert.Equal(["a", "b"], rig.Runner.Started);
    }

    [Fact]
    public async Task Stop_after_first_can_be_taken_back_while_waiting_for_the_window()
    {
        var rig = new Rig();
        var open = false;
        var waiting = new TaskCompletionSource();
        var queue = new EncodeQueue(rig.Runner, rig.Gate, () => open, async (_, ct) =>
        {
            waiting.TrySetResult();
            await Task.Delay(5, ct);
        })
        {
            StopAfterFirst = true,
        };
        var run = queue.RunAsync(["a", "b"]);
        await waiting.Task.WaitAsync(Timeout);

        Assert.True(queue.ContinueAfterCurrent());
        open = true;
        await rig.Runner.WaitForStartAsync();
        rig.Runner.Finish("a");
        await rig.Runner.WaitForStartAsync();
        rig.Runner.Finish("b");
        await run.WaitAsync(Timeout);

        Assert.Equal(["a", "b"], rig.Runner.Started);
    }

    [Fact]
    public void Transport_keys_do_nothing_when_idle()
    {
        var rig = new Rig();

        rig.Queue.Pause();
        rig.Queue.Skip();
        rig.Queue.Stop();
        rig.Queue.StopAfterCurrent();
        Assert.False(rig.Queue.ContinueAfterCurrent());

        Assert.Equal(QueueState.Idle, rig.Queue.State);
        Assert.Empty(rig.Gate.Calls.Where(c => c == "pause"));
    }
}
