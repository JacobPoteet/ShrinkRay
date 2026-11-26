namespace ShrinkRay.Core.Pipeline;

/// <summary>
/// The shows waiting to run after the one on the tape, in order, by folder name. A show is in it
/// once. Thread safe: the deck changes it while the run reads it.
/// </summary>
public sealed class ShowQueue
{
    private readonly List<string> _shows = [];
    private readonly object _lock = new();

    public ShowQueue(IEnumerable<string>? shows = null)
    {
        foreach (var show in shows ?? [])
        {
            Add(show);
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _shows.Count;
            }
        }
    }

    /// <summary>A copy of the queue, next show first.</summary>
    public IReadOnlyList<string> Items
    {
        get
        {
            lock (_lock)
            {
                return _shows.ToList();
            }
        }
    }

    /// <summary>The next show, or null when none waits.</summary>
    public string? Next
    {
        get
        {
            lock (_lock)
            {
                return _shows.Count > 0 ? _shows[0] : null;
            }
        }
    }

    /// <summary>The show's place in the queue, counting from 1, or null when it is not queued.</summary>
    public int? PositionOf(string show)
    {
        lock (_lock)
        {
            var index = IndexOf(show);
            return index < 0 ? null : index + 1;
        }
    }

    public bool Contains(string show) => PositionOf(show) is not null;

    /// <summary>Puts a show at the end. False when it is blank or already queued.</summary>
    public bool Add(string show)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(show) || IndexOf(show) >= 0)
            {
                return false;
            }

            _shows.Add(show);
            return true;
        }
    }

    public bool Remove(string show)
    {
        lock (_lock)
        {
            var index = IndexOf(show);
            if (index < 0)
            {
                return false;
            }

            _shows.RemoveAt(index);
            return true;
        }
    }

    private int IndexOf(string show) => _shows.FindIndex(s => string.Equals(s, show, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Hands an <see cref="EncodeQueue"/> its episodes one at a time.</summary>
public interface IEpisodeSource
{
    /// <summary>False once nothing is left to hand out. May answer true for a show that turns out to hold nothing.</summary>
    bool HasNext { get; }

    /// <summary>The next episode's path, or null when none is left.</summary>
    Task<string?> NextAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The episodes of the first show, then of each queued show in turn. A queued show is read when
/// its turn comes, so it includes whatever has arrived by then. The queue loop is the only caller.
/// </summary>
public sealed class ShowQueueSource : IEpisodeSource
{
    private readonly Queue<string> _pending;
    private readonly ShowQueue _shows;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _open;

    /// <param name="first">The episodes to run before any queued show.</param>
    /// <param name="open">Reads a queued show as its turn comes and returns the episodes to run.</param>
    public ShowQueueSource(IEnumerable<string> first, ShowQueue shows, Func<string, CancellationToken, Task<IReadOnlyList<string>>> open)
    {
        _pending = new Queue<string>(first);
        _shows = shows;
        _open = open;
    }

    public bool HasNext => _pending.Count > 0 || _shows.Count > 0;

    public async Task<string?> NextAsync(CancellationToken cancellationToken)
    {
        while (_pending.Count == 0)
        {
            if (_shows.Next is not { } show)
            {
                return null;
            }

            // The show leaves the queue only once it has been read, so a Stop here keeps it queued.
            var episodes = await _open(show, cancellationToken).ConfigureAwait(false);
            _shows.Remove(show);
            foreach (var episode in episodes)
            {
                _pending.Enqueue(episode);
            }
        }

        return _pending.Dequeue();
    }
}
