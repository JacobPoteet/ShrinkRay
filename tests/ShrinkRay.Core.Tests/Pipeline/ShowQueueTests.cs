using ShrinkRay.Core.Pipeline;

namespace ShrinkRay.Core.Tests.Pipeline;

public class ShowQueueTests
{
    [Fact]
    public void Shows_queue_once_in_order_ignoring_case()
    {
        var queue = new ShowQueue(["Scrubs", "", "scrubs"]);

        Assert.True(queue.Add("Bluey"));
        Assert.False(queue.Add("BLUEY"));

        Assert.Equal(["Scrubs", "Bluey"], queue.Items);
        Assert.Equal(2, queue.PositionOf("bluey"));
        Assert.Null(queue.PositionOf("Sintel"));
        Assert.True(queue.Remove("SCRUBS"));
        Assert.False(queue.Remove("Scrubs"));
        Assert.Equal("Bluey", queue.Next);
    }

    [Fact]
    public async Task The_source_hands_out_the_first_show_then_each_queued_one_as_its_turn_comes()
    {
        var queue = new ShowQueue(["Empty", "Second"]);
        var opened = new List<string>();
        var source = new ShowQueueSource(["a"], queue, (show, _) =>
        {
            opened.Add(show);
            return Task.FromResult<IReadOnlyList<string>>(show == "Second" ? ["b", "c"] : []);
        });

        Assert.True(source.HasNext);
        Assert.Equal("a", await source.NextAsync(default));
        Assert.Empty(opened);
        Assert.Equal("b", await source.NextAsync(default));
        Assert.Equal(["Empty", "Second"], opened);
        Assert.Equal(0, queue.Count);
        Assert.Equal("c", await source.NextAsync(default));
        Assert.False(source.HasNext);
        Assert.Null(await source.NextAsync(default));
    }

    [Fact]
    public async Task A_show_still_being_read_when_the_run_stops_stays_queued()
    {
        var queue = new ShowQueue(["Slow"]);
        using var stop = new CancellationTokenSource();
        var source = new ShowQueueSource([], queue, (_, token) =>
        {
            stop.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>(["never"]);
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() => source.NextAsync(stop.Token));

        Assert.Equal(["Slow"], queue.Items);
    }

    [Fact]
    public async Task Stop_while_the_next_show_is_read_ends_the_run()
    {
        var reading = new TaskCompletionSource();
        var source = new ShowQueueSource([], new ShowQueue(["Slow"]), async (_, token) =>
        {
            reading.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return ["never"];
        });
        var queue = new EncodeQueue(new NeverRunner(), null, () => true);

        var run = queue.RunAsync(source);
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        queue.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(QueueState.Idle, queue.State);
        Assert.False(queue.IsRunning);
    }

    private sealed class NeverRunner : IEpisodeRunner
    {
        public Task<PipelineResult> RunAsync(string sourcePath, IProgress<EpisodeProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No episode should run.");
    }
}
