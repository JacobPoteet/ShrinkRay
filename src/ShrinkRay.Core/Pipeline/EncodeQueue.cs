using ShrinkRay.Core.Processes;

namespace ShrinkRay.Core.Pipeline;

public enum QueueState
{
    Idle,
    Running,

    /// <summary>The Pause key is down. Tools are suspended, or the next episode waits.</summary>
    Paused,

    /// <summary>Outside the encode window. The next episode starts when the window opens.</summary>
    WaitingForWindow,
}

public enum EpisodeEnd
{
    /// <summary>The pipeline returned a result, which may still be a failure.</summary>
    Finished,

    /// <summary>Skip cancelled this episode and the queue moved on.</summary>
    Skipped,

    /// <summary>Stop cancelled this episode and ended the run.</summary>
    Stopped,
}

/// <param name="Result">The pipeline's result. Null when the episode was skipped or stopped.</param>
public sealed record EpisodeFinished(string Path, EpisodeEnd End, PipelineResult? Result);

/// <summary>
/// Runs a list of episodes through an <see cref="IEpisodeRunner"/> one at a time, with the
/// deck's transport: pause, skip and stop. Events fire on a worker thread.
/// </summary>
public sealed class EncodeQueue
{
    /// <summary>How often a queue outside the encode window checks the clock again.</summary>
    public static readonly TimeSpan WindowPoll = TimeSpan.FromSeconds(30);

    private readonly IEpisodeRunner _runner;
    private readonly IProcessGate? _gate;
    private readonly Func<bool> _mayStart;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly object _lock = new();

    private CancellationTokenSource? _run;
    private CancellationTokenSource? _episode;
    private TaskCompletionSource _resume = NewSignal();
    private bool _paused;
    private bool _skipRequested;
    private bool _stopAfterCurrent;
    private bool _stopCommitted;
    private bool _armFirst;

    /// <param name="mayStart">False holds the next episode, such as outside the encode window.</param>
    /// <param name="delay">Waits between window checks. Tests pass a fake.</param>
    public EncodeQueue(IEpisodeRunner runner, IProcessGate? gate, Func<bool> mayStart, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _runner = runner;
        _gate = gate;
        _mayStart = mayStart;
        _delay = delay ?? Task.Delay;
    }

    public event Action<QueueState>? StateChanged;

    public event Action<string>? EpisodeStarted;

    /// <summary>The episode's phase as each one starts, and HandBrake's progress during the encode.</summary>
    public event Action<string, EpisodeProgress>? Progress;

    public event Action<EpisodeFinished>? EpisodeFinished;

    public QueueState State { get; private set; }

    /// <summary>The episode the queue is working on, or null between episodes.</summary>
    public string? Current { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _run is not null;
            }
        }
    }

    /// <summary>
    /// Arms <see cref="StopAfterCurrent"/> as the first episode starts, so the run ends after it.
    /// <see cref="ContinueAfterCurrent"/> takes it back, before or during that episode.
    /// Set it before <see cref="RunAsync"/>.
    /// </summary>
    public bool StopAfterFirst { get; set; }

    /// <summary>Runs every path in order. Returns when the list is done or Stop is pressed.</summary>
    /// <exception cref="InvalidOperationException">A run is already going.</exception>
    public async Task RunAsync(IReadOnlyList<string> paths)
    {
        CancellationToken token;
        lock (_lock)
        {
            if (_run is not null)
            {
                throw new InvalidOperationException("The queue is already running.");
            }

            _run = new CancellationTokenSource();
            token = _run.Token;
            _stopAfterCurrent = false;
            _stopCommitted = false;
            _armFirst = StopAfterFirst;
        }

        try
        {
            foreach (var path in paths)
            {
                if (StopAfterCurrentRequested() || !await WaitToStartAsync(token).ConfigureAwait(false))
                {
                    break;
                }

                // Checked again after the wait, so a request made while the queue waited never
                // lets a whole new episode run.
                if (StopAfterCurrentRequested() || !await RunEpisodeAsync(path, token).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                _run?.Dispose();
                _run = null;
                _paused = false;
                _stopAfterCurrent = false;
                _stopCommitted = false;
                _resume.TrySetResult();
            }

            _gate?.Resume();
            Current = null;
            SetState(QueueState.Idle);
        }
    }

    /// <summary>Suspends the running tools, or holds the next episode. No effect when idle.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_run is null || _paused)
            {
                return;
            }

            _paused = true;
            _resume = NewSignal();
        }

        _gate?.Pause();
        SetState(QueueState.Paused);
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (!_paused)
            {
                return;
            }

            _paused = false;
            _resume.TrySetResult();
        }

        _gate?.Resume();
        if (Current is not null)
        {
            SetState(QueueState.Running);
        }
    }

    /// <summary>Abandons the current episode and moves to the next. The original is untouched.</summary>
    public void Skip()
    {
        CancellationTokenSource? episode;
        lock (_lock)
        {
            episode = _episode;
            if (episode is null)
            {
                return;
            }

            _skipRequested = true;
        }

        Resume();
        CancelQuietly(episode);
    }

    /// <summary>Abandons the current episode and ends the run.</summary>
    public void Stop()
    {
        CancellationTokenSource? run;
        lock (_lock)
        {
            run = _run;
        }

        // The queue's own pause hold stays: a queue held between episodes wakes on the cancel,
        // and releasing the hold first would let it start the next one. RunAsync clears it on exit.
        // Resume first so the gate forgets the pause; a suspended tool is then killed by the cancel.
        _gate?.Resume();
        if (run is not null)
        {
            CancelQuietly(run);
        }
    }

    /// <summary>
    /// Lets the current episode run to its end, then ends the run. Between episodes, with
    /// nothing to finish, it stops at once. A pause is released so the episode can finish.
    /// </summary>
    public void StopAfterCurrent()
    {
        CancellationTokenSource? run;
        bool betweenEpisodes;
        lock (_lock)
        {
            run = _run;
            if (run is null)
            {
                return;
            }

            _stopAfterCurrent = true;
            betweenEpisodes = _episode is null;
        }

        Resume();
        if (betweenEpisodes)
        {
            CancelQuietly(run);
        }
    }

    /// <summary>
    /// Takes back <see cref="StopAfterCurrent"/> so the run goes on through the list.
    /// </summary>
    /// <returns>
    /// True when the request was cleared and the run carries on. False when there was nothing
    /// to clear, or the run is already ending: the episode finished and the queue committed to
    /// stopping, or the request stopped the run at once.
    /// </returns>
    public bool ContinueAfterCurrent()
    {
        lock (_lock)
        {
            if (_run is not null && _armFirst && !_stopAfterCurrent && !_run.IsCancellationRequested)
            {
                // Taken back before the first episode started.
                _armFirst = false;
                return true;
            }

            _armFirst = false;
            if (_run is null || !_stopAfterCurrent || _stopCommitted || _run.IsCancellationRequested)
            {
                return false;
            }

            _stopAfterCurrent = false;
            return true;
        }
    }

    private static void CancelQuietly(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The episode or run ended at the same moment.
        }
    }

    private async Task<bool> WaitToStartAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Task? resume = null;
            lock (_lock)
            {
                if (_paused)
                {
                    resume = _resume.Task;
                }
            }

            try
            {
                if (resume is not null)
                {
                    SetState(QueueState.Paused);
                    await resume.WaitAsync(token).ConfigureAwait(false);
                    continue;
                }

                if (!_mayStart())
                {
                    SetState(QueueState.WaitingForWindow);
                    await _delay(WindowPoll, token).ConfigureAwait(false);
                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            return true;
        }

        return false;
    }

    /// <returns>False when Stop ended the run.</returns>
    private async Task<bool> RunEpisodeAsync(string path, CancellationToken runToken)
    {
        using var episode = CancellationTokenSource.CreateLinkedTokenSource(runToken);
        lock (_lock)
        {
            _episode = episode;
            _skipRequested = false;
            if (_armFirst)
            {
                // In the same lock that names the episode, so it cannot finish unarmed.
                _armFirst = false;
                _stopAfterCurrent = true;
            }
        }

        Current = path;
        SetState(_paused ? QueueState.Paused : QueueState.Running);
        EpisodeStarted?.Invoke(path);

        var progress = new InlineProgress<EpisodeProgress>(p => Progress?.Invoke(path, p));
        try
        {
            var result = await Task.Run(() => _runner.RunAsync(path, progress, episode.Token), CancellationToken.None).ConfigureAwait(false);
            EpisodeFinished?.Invoke(new EpisodeFinished(path, EpisodeEnd.Finished, result));
            return true;
        }
        catch (OperationCanceledException) when (!runToken.IsCancellationRequested && SkipWasRequested())
        {
            EpisodeFinished?.Invoke(new EpisodeFinished(path, EpisodeEnd.Skipped, null));
            return true;
        }
        catch (OperationCanceledException)
        {
            EpisodeFinished?.Invoke(new EpisodeFinished(path, EpisodeEnd.Stopped, null));
            return false;
        }
        catch (Exception ex)
        {
            EpisodeFinished?.Invoke(new EpisodeFinished(path, EpisodeEnd.Finished, new PipelineResult(PipelineOutcome.Failed, ex.Message)));
            return true;
        }
        finally
        {
            lock (_lock)
            {
                _episode = null;
            }

            Current = null;
        }
    }

    /// <summary>
    /// Reads the request and, when it is set, commits to ending the run in the same lock, so
    /// <see cref="ContinueAfterCurrent"/> can never clear a request the loop already acted on.
    /// </summary>
    private bool StopAfterCurrentRequested()
    {
        lock (_lock)
        {
            if (_stopAfterCurrent)
            {
                _stopCommitted = true;
            }

            return _stopAfterCurrent;
        }
    }

    private bool SkipWasRequested()
    {
        lock (_lock)
        {
            return _skipRequested;
        }
    }

    private void SetState(QueueState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(state);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
