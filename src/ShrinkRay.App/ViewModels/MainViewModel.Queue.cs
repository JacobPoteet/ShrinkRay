using ShrinkRay.App.Mvvm;
using ShrinkRay.Core.Library;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Settings;

namespace ShrinkRay.App.ViewModels;

/// <summary>
/// The show queue. Encode runs the show on the tape, then each queued show in order. The tape
/// switches as each one's turn comes, and the shows are read then, so late arrivals are included.
/// The queue is saved with the deck's preferences and waits for Encode after a restart.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly ShowQueue _showQueue;

    /// <summary>The engine for the show that is running. Each show gets its own, with its own settings.</summary>
    private IEpisodeRunner? _runner;

    /// <summary>Adds the show in the track list to the queue, or takes it out.</summary>
    public RelayCommand ToggleQueueCommand { get; private set; } = null!;

    /// <summary>The queued shows' folder names, next first.</summary>
    public IReadOnlyList<string> QueuedShows => _showQueue.Items;

    public bool HasQueue => _showQueue.Count > 0;

    /// <summary>The line on monitor A about what runs after this show. Empty when nothing is queued.</summary>
    public string QueueLine => HasQueue ? $"QUEUE {QueueCount} · {Format.Duration(QueueTime)}".ToUpperInvariant() : "";

    /// <summary>The same in full, one queued show a line.</summary>
    public string QueueDetail => string.Join("\n", _showQueue.Items.Select((name, i) => $"{i + 1}. {name}"));

    private string QueueCount => _showQueue.Count == 1 ? "1 show" : $"{_showQueue.Count} shows";

    /// <summary>The show whose episodes the track list shows: the browsed one during a run, else the tape's.</summary>
    private ShowScan? ShownShow => _browsed ?? _loaded;

    /// <summary>The track list's show can join or leave the queue. The one being encoded cannot.</summary>
    public bool CanQueueShown => ShownShow is { } show && CanQueue(show.Name);

    public string QueueKeyText => ShownShow is { } show && _showQueue.Contains(show.Name) ? "Remove from queue" : "Add to queue";

    private TimeSpan QueueTime => TimeSpan.FromSeconds(_showQueue.Items.Sum(SecondsFor));

    private double SecondsFor(string show)
    {
        if (!_scans.TryGetValue(show, out var scan))
        {
            return 0;
        }

        var summary = ShowSummary.From(scan, Classify);
        var output = _settings.ReplaceOriginals ? (long)(summary.QueueableBytes * RatioFor(show, _settings.CqForShow(show))) : 0;
        return EpisodeEstimate.For(summary.QueueableFrames, summary.QueueableBytes, output, _rates).TotalSeconds;
    }

    private bool CanQueue(string show)
    {
        if (IsDeckHeld || (IsEncoding && string.Equals(show, _loaded?.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // A show with nothing left to do has no row, and nothing to queue.
        return _showQueue.Contains(show) || Shows.Any(r => string.Equals(r.FolderName, show, StringComparison.OrdinalIgnoreCase));
    }

    private void ToggleQueued(string show)
    {
        if (!CanQueue(show))
        {
            return;
        }

        if (!_showQueue.Remove(show))
        {
            _showQueue.Add(show);
        }

        SaveQueue();
    }

    private void SaveQueue()
    {
        _prefs = _prefs with { Queue = _showQueue.Items };
        SavePreferences();
        RefreshQueue();
    }

    /// <summary>Drops queued shows a full scan found nothing to do in, such as one finished elsewhere.</summary>
    private void PruneQueue()
    {
        var gone = _showQueue.Items
            .Where(show => !Shows.Any(r => string.Equals(r.FolderName, show, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (gone.Count == 0)
        {
            return;
        }

        foreach (var show in gone)
        {
            _showQueue.Remove(show);
        }

        SaveQueue();
    }

    private void RefreshQueue()
    {
        foreach (var row in Shows)
        {
            row.QueuePosition = _showQueue.PositionOf(row.FolderName);
            (row.QueueCommand as RelayCommand)?.Refresh();
        }

        Raise(nameof(QueuedShows), nameof(HasQueue), nameof(QueueLine), nameof(QueueDetail), nameof(CanQueueShown),
            nameof(QueueKeyText), nameof(ShowLeftDetail));
        ToggleQueueCommand.Refresh();
    }

    // ---- A run through the queue ----

    /// <summary>Takes a fresh snapshot of the settings for a show about to run, and its engine.</summary>
    private ShrinkRaySettings PrepareRun(string show)
    {
        var snapshot = SettingsStore.Normalize(_settings);
        _runSettings = snapshot;
        _runCq = snapshot.CqForShow(show);
        _runner = _s.CreateRunner(snapshot);
        return snapshot;
    }

    /// <summary>
    /// Reads a queued show as its turn comes and puts it on the tape. Called from the queue's
    /// thread. Empty when the show left the queue meanwhile or has nothing to do.
    /// </summary>
    /// <exception cref="ShowUnavailableException">The library cannot be reached.</exception>
    private async Task<IReadOnlyList<string>> OpenQueuedShowAsync(string show, CancellationToken cancellationToken)
    {
        var (folder, root) = await OnDeck(() => (_showQueue.Contains(show) ? FolderFor(show) : null, _settings.LibraryRoot))
            .ConfigureAwait(false);
        if (folder is null)
        {
            return [];
        }

        ShowScan scan;
        try
        {
            scan = await Task.Run(() => _scanner.ScanShowAsync(folder, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await OnDeck(() => Notice = $"{show} was skipped: {ex.Message}").ConfigureAwait(false);
            return [];
        }

        // A share that dropped lists nothing, which must not empty the queue show by show.
        if (scan.Episodes.Count == 0 && !_s.DirectoryExists(root))
        {
            throw new ShowUnavailableException(
                $"The library could not be reached when {show}'s turn came, so the run stopped. It stays queued.");
        }

        return await OnDeck(() => SwitchTape(scan, cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>
    /// A show's folder: from its scan, or from the library when a scan still running has not
    /// reached it yet.
    /// </summary>
    private string FolderFor(string show)
    {
        if (_scans.TryGetValue(show, out var scan))
        {
            return scan.Folder;
        }

        var root = _settings.LibraryRoot.TrimEnd('\\');
        return string.Equals(Path.GetFileName(root), show, StringComparison.OrdinalIgnoreCase)
            ? _settings.LibraryRoot
            : Path.Combine(_settings.LibraryRoot, show);
    }

    private IReadOnlyList<string> SwitchTape(ShowScan scan, CancellationToken cancellationToken)
    {
        // A Stop that landed while the show was being read leaves it queued, and the tape as it is.
        cancellationToken.ThrowIfCancellationRequested();
        SaveCache();
        AddScan(scan);
        if (!_showQueue.Contains(scan.Name))
        {
            return [];
        }

        var tracks = scan.Episodes.Select(MakeTrack).ToList();
        var paths = tracks.Where(t => t.IsQueueable).Select(t => t.Path).ToList();
        _showQueue.Remove(scan.Name);
        SaveQueue();
        if (paths.Count == 0)
        {
            Notice = $"{scan.Name} was skipped: nothing to encode, or its folder could not be read.";
            return [];
        }

        var leaving = _loaded;
        PrepareRun(scan.Name);

        // The payoff belongs to the show it happened in.
        EndBank();
        SavedRoll = null;
        LifetimeSavedRoll = null;
        Raise(nameof(SavedRoll), nameof(LifetimeSavedRoll));

        _loaded = scan;
        foreach (var row in Shows)
        {
            row.IsLoaded = string.Equals(row.FolderName, scan.Name, StringComparison.OrdinalIgnoreCase);
        }

        _tracks.Clear();
        _tracks.AddRange(tracks);

        // A browse of another show carries on. Otherwise the list and the cursor follow the tape.
        if (_browsed is null || string.Equals(_browsed.Name, scan.Name, StringComparison.OrdinalIgnoreCase))
        {
            _browsed = null;
            _browseTracks = null;
            Set(ref _selectedShow, Shows.FirstOrDefault(r => r.IsLoaded), nameof(SelectedShow));
            ListTracksFrom(_tracks);
            Raise(nameof(IsBrowsing), nameof(TracksHeading), nameof(BackText));
        }

        LogShow(scan.Name, paths.Count);
        _prefs = _prefs with { LastShow = scan.Name };
        SavePreferences();
        _estimator = new TimeLeftEstimator(_estimator?.Speed ?? _rates.EncodeFps);
        _estimator.Rebase(_s.Now(), FramesLeft(), OtherLeft());
        SyncQualityChips();
        RefreshAll();

        if (leaving is not null)
        {
            // The finished show's row catches up with what changed on disk.
            _ = RescanShowAsync(leaving.Folder);
        }

        return paths;
    }

    /// <summary>Runs <paramref name="act"/> on the deck's thread and waits for its answer.</summary>
    private Task<T> OnDeck<T>(Func<T> act)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _s.Dispatch(() =>
        {
            try
            {
                done.SetResult(act());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        return done.Task;
    }

    /// <summary>Hands each episode to the engine of the show it belongs to.</summary>
    private sealed class CurrentRunner(Func<IEpisodeRunner> current) : IEpisodeRunner
    {
        public Task<PipelineResult> RunAsync(string sourcePath, IProgress<EpisodeProgress>? progress, CancellationToken cancellationToken) =>
            current().RunAsync(sourcePath, progress, cancellationToken);
    }
}
