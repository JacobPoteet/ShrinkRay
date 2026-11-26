using ShrinkRay.App.Mvvm;
using ShrinkRay.App.Services;
using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Library;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Setup;
using ShrinkRay.Core.Swap;
using ShrinkRay.Core.Verification;
using ShrinkRay.Theme;
using ShrinkRay.Theme.Controls;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.App.ViewModels;

public sealed partial class MainViewModel
{
    // ---- Startup ----

    /// <summary>
    /// Runs journal recovery first. Then a first start opens setup, and any other start scans
    /// the library and reloads the last tape. Encode stays off until recovery has finished, and
    /// while setup is open.
    /// </summary>
    public async Task StartAsync()
    {
        _s.ApplyTheme(_prefs.Phosphor, _prefs.Chassis);
        if (string.IsNullOrWhiteSpace(_settings.ScratchFolder) && !string.IsNullOrWhiteSpace(_defaultScratch))
        {
            _settings.ScratchFolder = _defaultScratch;
            Save();
            Raise(nameof(ScratchFolder));
        }

        RefreshSystemLamps();
        _ = CheckGpuAsync();

        SetRecovering(true);
        try
        {
            var actions = await _s.Recover(SettingsStore.Normalize(_settings), CancellationToken.None);
            _recoveryReviews = actions.Count(a => a.Kind == RecoveryKind.NeedsReview);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _recoveryFailed = true;
            Notice = "Recovery could not finish: " + ex.Message;
        }
        finally
        {
            SetRecovering(false);
        }

        RefreshSystemLamps();

        if (FirstRunFlow.NeedsSetup(_settings))
        {
            // Setup scans the library itself, once the self-test has found the tools.
            BeginSetup();
            return;
        }

        await ScanLibraryAsync(_prefs.LastShow);
    }

    private void SetRecovering(bool value)
    {
        _isRecovering = value;
        Raise(nameof(MonitorMessage));
        RefreshTransport();
        RescanCommand.Refresh();
    }

    // ---- Library ----

    /// <summary>Scans every show folder under the library root. <paramref name="loadShow"/> goes first and loads when found.</summary>
    public async Task ScanLibraryAsync(string? loadShow = null)
    {
        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        var token = cts.Token;

        _scans.Clear();
        _hidden.Clear();
        Shows.Clear();
        SelectedShow = null;
        RefreshSystemLamps();

        var root = _settings.LibraryRoot;
        if (string.IsNullOrWhiteSpace(root) || !_s.DirectoryExists(root))
        {
            // A scan this one cancelled no longer clears the flag itself.
            _isScanning = false;
            RefreshLibraryText();
            return;
        }

        _isScanning = true;
        try
        {
            var folders = (await Task.Run(() => _scanner.ListShowFolders(root), token)).ToList();
            if (loadShow is not null)
            {
                var first = folders.FindIndex(f => string.Equals(Path.GetFileName(f), loadShow, StringComparison.OrdinalIgnoreCase));
                if (first > 0)
                {
                    var folder = folders[first];
                    folders.RemoveAt(first);
                    folders.Insert(0, folder);
                }
            }

            for (var i = 0; i < folders.Count; i++)
            {
                _scanStatus = $"SCANNING {i + 1}/{folders.Count}";
                RefreshLibraryText();
                var folder = folders[i];
                var scan = await Task.Run(() => _scanner.ScanShowAsync(folder, token), token);
                token.ThrowIfCancellationRequested();
                AddScan(scan);
                if (string.Equals(scan.Name, loadShow, StringComparison.OrdinalIgnoreCase) && !IsEncoding)
                {
                    Select(scan);
                }
            }

            SaveCache();

            // A share that listed nothing may only be offline for a moment, so the queue waits.
            if (folders.Count > 0 && !IsEncoding)
            {
                PruneQueue();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer scan took over.
        }
        finally
        {
            if (_scanCts == cts)
            {
                _isScanning = false;
                RefreshLibraryText();
            }
        }

        SelectedShow ??= Shows.FirstOrDefault(r => r.IsLoaded);
    }

    /// <summary>Puts a show in the deck and moves the cursor to its row. A show with no row, such as one with nothing left to do, loads anyway.</summary>
    private void Select(ShowScan scan)
    {
        SelectedShow = Shows.FirstOrDefault(r => string.Equals(r.FolderName, scan.Name, StringComparison.OrdinalIgnoreCase));
        if (!ReferenceEquals(_loaded, scan))
        {
            LoadShow(scan);
        }
    }

    private void AddScan(ShowScan scan)
    {
        _scans[scan.Name] = scan;
        var summary = ShowSummary.From(scan, Classify);
        var existing = Shows.FirstOrDefault(r => string.Equals(r.FolderName, scan.Name, StringComparison.OrdinalIgnoreCase));
        _hidden.Remove(scan.Name);

        if (summary.IsCandidate)
        {
            if (existing is not null)
            {
                existing.Update(scan, summary);
                existing.Detail = DetailFor(existing);
            }
            else
            {
                var name = scan.Name;
                var row = new ShowRowViewModel(scan, summary)
                {
                    IsLoaded = string.Equals(scan.Name, _loaded?.Name, StringComparison.OrdinalIgnoreCase),
                    QueuePosition = _showQueue.PositionOf(name),
                    QueueCommand = new RelayCommand(_ => ToggleQueued(name), _ => CanQueue(name)),
                };
                row.Detail = DetailFor(row);
                var index = 0;
                while (index < Shows.Count && string.Compare(Shows[index].FolderName, scan.Name, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    index++;
                }

                Shows.Insert(index, row);
            }
        }
        else
        {
            if (existing is not null)
            {
                if (_selectedShow == existing)
                {
                    SelectedShow = null;
                }

                Shows.Remove(existing);
            }

            if (summary.EpisodeCount > 0)
            {
                _hidden[scan.Name] = summary.Hidden;
            }
        }

        RefreshLibraryText();
    }

    private EpisodeStatus Classify(EpisodeRecord episode) => EpisodeStatusRules.Classify(
        episode, _s.Journal.Latest(episode.Path), _s.Ledger.Get(episode.Path) is not null, _settings.ReplaceOriginals);

    /// <summary>
    /// Reads every scanned episode's status again, after a change that moves it, such as
    /// replacing originals turning on. Runs while idle.
    /// </summary>
    private void Reclassify()
    {
        foreach (var scan in _scans.Values.ToList())
        {
            AddScan(scan);
        }

        if (_loaded is null)
        {
            return;
        }

        _loaded = _scans.GetValueOrDefault(_loaded.Name) ?? _loaded;
        var season = _selectedSeason;
        _tracks.Clear();
        _tracks.AddRange(_loaded.Episodes.Select(MakeTrack));
        ListTracksFrom(_tracks, season);
        RefreshAll();
    }

    private void ChooseLibrary()
    {
        var folder = _s.FolderPicker?.PickFolder("Choose the TV library or one show's folder", _settings.LibraryRoot);
        if (folder is not null)
        {
            SetLibraryRoot(folder);
        }
    }

    /// <summary>
    /// Makes a folder the library: a TV root lists its shows, and a show folder lists just that
    /// show and loads it.
    /// </summary>
    private void SetLibraryRoot(string folder)
    {
        _settings.LibraryRoot = folder.TrimEnd('\\', '/');
        if (_settings.LibraryRoot.EndsWith(':'))
        {
            _settings.LibraryRoot += "\\";
        }

        Save();
        Raise(nameof(LibraryRoot));
        var show = LibraryScanner.IsShowFolder(_s.Disk, _settings.LibraryRoot) ? Path.GetFileName(_settings.LibraryRoot.TrimEnd('\\')) : null;
        _ = ScanLibraryAsync(show ?? _loaded?.Name);
    }

    /// <summary>A dropped folder is taken while idle, and during setup only on the library card.</summary>
    public bool CanDropFolder => !IsEncoding && !_isFirstEpisodeCardOpen && (!_flow.IsRunning || IsLibraryCard);

    /// <summary>
    /// Handles a folder dropped on the window. A show already listed loads as a tape. Any other
    /// folder becomes the library, as Choose does: a show folder (or a season folder, which
    /// stands for its show) is the library's one show, and anything else is a TV root.
    /// </summary>
    /// <returns>False when the drop was refused, such as during an encode.</returns>
    public bool DropFolder(string path)
    {
        if (!CanDropFolder || !_s.DirectoryExists(path))
        {
            return false;
        }

        var folder = path.TrimEnd('\\', '/');
        if (ShowFolder.IsSeasonFolder(Path.GetFileName(folder)) && Path.GetDirectoryName(folder) is { } showFolder)
        {
            folder = showFolder;
        }

        // On setup's library card a drop always names the library, even a show already listed.
        if (!IsLibraryCard
            && LibraryScanner.IsShowFolder(_s.Disk, folder)
            && _scans.TryGetValue(Path.GetFileName(folder), out var scan)
            && string.Equals(scan.Folder.TrimEnd('\\', '/'), folder, StringComparison.OrdinalIgnoreCase))
        {
            Select(scan);
            return true;
        }

        SetLibraryRoot(folder);
        return true;
    }

    // ---- Tape ----

    private void LoadShow(ShowScan scan)
    {
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
        _tracks.AddRange(scan.Episodes.Select(MakeTrack));
        _browsed = null;
        _browseTracks = null;
        ListTracksFrom(_tracks);
        Raise(nameof(IsBrowsing), nameof(TracksHeading), nameof(BackText));

        SyncQualityChips();
        _prefs = _prefs with { LastShow = scan.Name };
        SavePreferences();
        Notice = "";
        RefreshAll();
    }

    private TrackViewModel MakeTrack(EpisodeRecord episode)
    {
        var last = _s.Journal.Latest(episode.Path);
        return TrackViewModel.From(episode, Classify(episode), _s.Ledger.Get(episode.Path), last?.Reason, last?.Timestamp);
    }

    /// <summary>The episodes the track list is built from: the browsed show's during a browse, else the tape's.</summary>
    private List<TrackViewModel> ListTracks => _browseTracks ?? _tracks;

    /// <summary>Rebuilds the season chips and the list from a show's tracks, opening the first season with work to do.</summary>
    private void ListTracksFrom(List<TrackViewModel> tracks, int? preferSeason = null)
    {
        Seasons.Clear();
        foreach (var season in tracks.Select(t => t.Record.Season).Distinct().OrderBy(s => s == 0 ? int.MaxValue : s))
        {
            Seasons.Add(new SeasonChipViewModel(season, SelectSeason));
        }

        var first = preferSeason is { } p && Seasons.Any(c => c.Number == p)
            ? p
            : tracks.FirstOrDefault(t => t.IsQueueable)?.Record.Season ?? Seasons.FirstOrDefault()?.Number ?? 0;
        ShowSeason(first);
    }

    // ---- Browsing ----

    /// <summary>
    /// Opens another show in the track list during a run, for viewing. Its rows read the journal
    /// and the ledger as they stand, and nothing of the run reaches them.
    /// </summary>
    private void Browse(ShowRowViewModel row)
    {
        _browsed = row.Scan;
        _browseTracks = row.Scan.Episodes.Select(MakeTrack).ToList();
        ListTracksFrom(_browseTracks);
        Raise(nameof(IsBrowsing), nameof(TracksHeading), nameof(BackText));
        SyncQualityChips();
        RefreshQueue();
    }

    /// <summary>Goes back to listing the show on the tape.</summary>
    private void EndBrowse()
    {
        if (_browsed is null)
        {
            return;
        }

        _browsed = null;
        _browseTracks = null;
        ListTracksFrom(_tracks, _current?.Record.Season);
        Raise(nameof(IsBrowsing), nameof(TracksHeading), nameof(BackText));
        SyncQualityChips();
        RefreshQueue();
    }

    /// <summary>Sets the Quality row to the show it is about.</summary>
    private void SyncQualityChips()
    {
        _cqIndex = NearestDetent(_settings.CqForShow(ShownShow?.Name));
        Raise(nameof(CqIndex), nameof(Cq), nameof(CqCaption), nameof(HasCqOverride), nameof(CanEditQuality), nameof(EstimateText));
        RefreshCqChips();
    }

    private void BackToRun()
    {
        if (_browsed is null)
        {
            return;
        }

        // Moving the cursor to the loaded row ends the browse; a loaded show with no row ends it directly.
        if (Shows.FirstOrDefault(r => r.IsLoaded) is { } loaded)
        {
            SelectedShow = loaded;
        }

        EndBrowse();
    }

    private void SelectSeason(int season) => ShowSeason(season);

    private void ShowSeason(int season)
    {
        _selectedSeason = season;
        foreach (var chip in Seasons)
        {
            chip.SelectQuietly(chip.Number == season);
        }

        Tracks.Clear();
        foreach (var track in ListTracks.Where(t => t.Record.Season == season))
        {
            Tracks.Add(track);
        }

        RefreshSeasonLamps();
        Raise(nameof(SelectedSeason));
    }

    // ---- Transport ----

    private void Encode()
    {
        if (_queue is { } running)
        {
            running.Resume();
            RefreshTransport();
            return;
        }

        if (_loaded is null || _isRecovering || IsDeckHeld)
        {
            RefreshTransport();
            return;
        }

        var tracks = _tracks.Where(t => t.IsQueueable).ToList();
        var queuedAfter = _showQueue.Items.Any(s => !string.Equals(s, _loaded.Name, StringComparison.OrdinalIgnoreCase));
        var trial = FirstRunFlow.IsFirstEpisodePending(_settings);
        if (tracks.Count == 0 && (trial || !queuedAfter))
        {
            Notice = "Nothing left to encode in this show.";
            RefreshTransport();
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.ScratchFolder))
        {
            Notice = "Choose a scratch folder before encoding.";
            IsSettingsOpen = true;
            RefreshTransport();
            return;
        }

        var snapshot = PrepareRun(_loaded.Name);
        Notice = "";

        // The show on the tape is running now, so it no longer waits.
        if (_showQueue.Remove(_loaded.Name))
        {
            SaveQueue();
        }

        var queue = new EncodeQueue(
            new CurrentRunner(() => _runner!),
            _s.Gate,
            () => snapshot.IsInsideEncodeWindow(TimeOnly.FromDateTime(_s.Now())),
            _s.Delay)
        {
            StopAfterFirst = trial,
        };
        _isTrialRun = trial;
        _isTrialJudged = false;
        _report = null;
        queue.EpisodeStarted += path => _s.Dispatch(() => OnEpisodeStarted(path));
        queue.Progress += (path, p) => _s.Dispatch(() => OnProgress(path, p));
        queue.EpisodeFinished += f => _s.Dispatch(() => OnEpisodeFinished(f));
        queue.StateChanged += _ => _s.Dispatch(RefreshTransport);
        _estimator = new TimeLeftEstimator(_rates.EncodeFps);
        _estimator.Rebase(_s.Now(), FramesLeft(), OtherLeft());
        _queue = queue;
        // The trial run starts with its stop latched; the Stop card can take it back.
        IsStopArmed = trial;
        RefreshTransport();
        SyncQualityChips();
        RefreshQueue();

        // The trial stops after one episode, so it never reaches the queue.
        var source = new ShowQueueSource(tracks.Select(t => t.Path), trial ? new ShowQueue() : _showQueue, OpenQueuedShowAsync);
        CurrentRun = RunQueueAsync(queue, source);
    }

    private async Task RunQueueAsync(EncodeQueue queue, IEpisodeSource source)
    {
        try
        {
            await queue.RunAsync(source).ConfigureAwait(false);
        }
        finally
        {
            _s.Dispatch(OnRunFinished);
        }
    }

    private void TogglePause()
    {
        if (_queue is not { } queue)
        {
            RefreshTransport();
            return;
        }

        if (queue.State == QueueState.Paused)
        {
            queue.Resume();
        }
        else
        {
            queue.Pause();
        }

        RefreshTransport();
    }

    private void Skip()
    {
        _queue?.Skip();
        RefreshTransport();
    }

    private TrackViewModel? Find(string path) =>
        _tracks.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));

    private void OnEpisodeStarted(string path)
    {
        _current = Find(path);
        ResetEpisodeProgress();
        _phase = EpisodePhase.Probing;
        BitrateMbps = 0;
        if (_current is null)
        {
            return;
        }

        _timing = TimingFor(_current);
        _meter = new PhaseMeter(_current.Record.FrameCount ?? 0, _s.Now());
        _current.SetEncoding(0, 0, Ratio);
        _estimator?.Rebase(_s.Now(), FramesLeft(), OtherLeft());
        if (!IsBrowsing && _current.Record.Season != _selectedSeason)
        {
            ShowSeason(_current.Record.Season);
        }

        RefreshDeck();
        RefreshTransport();
        RefreshClosing();
    }

    private void OnProgress(string path, EpisodeProgress progress)
    {
        if (_current is null || !string.Equals(_current.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (progress.Phase != _phase)
        {
            _step = 0;
            if (_meter is { } meter)
            {
                _rates = meter.Enter(progress.Phase, _s.Now(), _rates);
            }
        }

        _phase = progress.Phase;
        if (progress.TotalBytes is { } total)
        {
            _meter?.Moving(total);
        }
        if (progress.Encode is { } encode)
        {
            _progress = Math.Clamp(encode.Percent / 100, 0, 1);
            _fps = encode.Fps ?? encode.AverageFps;
        }
        else if (_phase == EpisodePhase.Copying)
        {
            // Nothing is encoded yet; the reels and tape time wait at the start.
            _progress = 0;
            _fps = null;
        }
        else if (_phase > EpisodePhase.Encoding)
        {
            // The encode is done; the reels stay wound to the end while the file is checked and swapped.
            _progress = 1;
            _fps = null;
        }

        if (progress.Encode is not null)
        {
            _step = _progress;
        }
        else if (progress.Fraction is { } done)
        {
            _step = Math.Clamp(done, 0, 1);
        }

        if (_timing is { } timing)
        {
            _overall = Math.Max(_overall, timing.Fraction(_phase, _step));
        }

        _current.SetEncoding(_overall, _progress, Ratio);
        _estimator?.Sample(_s.Now(), FramesLeft(), _fps, OtherLeft());
        RefreshReadouts();
        RefreshTransport();

        // The swap may start while the confirm card is open, or after a confirmed close began.
        RefreshClosing();
    }

    private void OnEpisodeFinished(EpisodeFinished finished)
    {
        var track = Find(finished.Path);
        var savedBefore = SavedDigits;
        var lifetimeBefore = LifetimeSavedDigits;
        if (track is not null)
        {
            switch (finished.End)
            {
                case EpisodeEnd.Skipped:
                    track.SetSkipped("Skipped from the deck. The original is untouched and the next run tries it again.");
                    break;
                case EpisodeEnd.Stopped:
                    track.SetQueued();
                    break;
                default:
                    ApplyResult(track, finished.Result!);
                    break;
            }
        }

        LearnFrom(finished);
        if (_current == track)
        {
            _current = null;
            ResetEpisodeProgress();
        }

        _estimator?.Rebase(_s.Now(), FramesLeft(), OtherLeft());
        JudgeTrial(track, finished);
        RefreshAll();
        if (track is not null && finished.Result?.Outcome == PipelineOutcome.Replaced)
        {
            Bank(track, savedBefore, lifetimeBefore);
        }

        RefreshClosing();
    }

    private void ResetEpisodeProgress()
    {
        _progress = 0;
        _overall = 0;
        _step = 0;
        _fps = null;
        _timing = null;
    }

    /// <summary>Closes the last step of an episode that passed its checks, and saves what its steps taught.</summary>
    private void LearnFrom(EpisodeFinished finished)
    {
        if (_meter is not { } meter)
        {
            return;
        }

        _meter = null;
        if (finished.Result?.Verification is { } verification && verification.Outcome != VerificationOutcome.Fail
            && finished.Result.Outcome is PipelineOutcome.Replaced or PipelineOutcome.KeptOriginal)
        {
            _rates = meter.Enter(EpisodePhase.Swapping, _s.Now(), _rates);
        }

        Try(() => _s.SaveRates(_rates), "The step speeds could not be saved");
    }

    // ---- Payoff ----

    /// <summary>A file was replaced: stamp its row, light the Banked lamp and count both totals up.</summary>
    private void Bank(TrackViewModel track, string savedBefore, string lifetimeBefore)
    {
        EndBank();
        _banked = track;
        _bankedUntil = _s.Now() + BankedHold;
        _bankedCount++;
        track.Stamp();
        SavedRoll = new CounterRoll(savedBefore, _bankedCount);
        LifetimeSavedRoll = new CounterRoll(lifetimeBefore, _bankedCount);
        Raise(nameof(IsBanked), nameof(BankedLamp), nameof(SavedRoll), nameof(LifetimeSavedRoll));
    }

    /// <summary>Puts the lamp out once the payoff has had its time. The stamp stays on the row.</summary>
    private void EndBank()
    {
        if (_banked is null)
        {
            return;
        }

        _banked.SettleStamp();
        _banked = null;
        Raise(nameof(IsBanked), nameof(BankedLamp));
    }

    private void ApplyResult(TrackViewModel track, PipelineResult result)
    {
        switch (result.Outcome)
        {
            case PipelineOutcome.Replaced:
                var newBytes = _s.FileSize(track.Path) ?? (long)(track.OriginalBytes * Ratio);
                _s.Ledger.Record(new LedgerEntry(track.Path, track.OriginalBytes, newBytes, _runCq, DateTimeOffset.Now));
                track.SetShrunk(newBytes);
                RaiseLifetime();
                break;
            case PipelineOutcome.KeptOriginal:
                track.SetKept(result.Verification?.Savings, result.Message);
                break;
            case PipelineOutcome.Skipped:
                track.SetSkipped(result.Message);
                break;
            case PipelineOutcome.SourceChanged:
                track.SetChanged(result.Message);
                break;
            default:
                track.SetFailed(result.Message, _s.Now());
                break;
        }
    }

    private void OnRunFinished()
    {
        _queue = null;
        _estimator = null;
        ResetStopping();

        // The deck goes back to normal: the show that encoded stays loaded, and the cursor returns to it.
        EndBrowse();
        if (_selectedShow is { IsLoaded: false } && Shows.FirstOrDefault(r => r.IsLoaded) is { } loaded)
        {
            SelectedShow = loaded;
        }

        _current = null;
        _meter = null;
        ResetEpisodeProgress();
        BitrateMeterRest();
        _runner = null;
        SyncQualityChips();
        RefreshAll();
        RefreshClosing();
        FinishTrial();

        if (_isClosing)
        {
            CloseReady?.Invoke(this, EventArgs.Empty);
            return;
        }

        // Replaced files changed on disk, so a rescan of this show brings the list up to date.
        if (_loaded is { } show)
        {
            _ = RescanShowAsync(show.Folder);
        }
    }

    private async Task RescanShowAsync(string folder)
    {
        try
        {
            var scan = await Task.Run(() => _scanner.ScanShowAsync(folder, CancellationToken.None));
            _s.Dispatch(() =>
            {
                AddScan(scan);
                SaveCache();
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next full scan picks it up.
        }
    }

    // ---- Instruments ----

    /// <summary>
    /// Called twice a second by the window. Reads the output file's growth for the bitrate
    /// needle and polls the encoder load.
    /// </summary>
    public void Tick()
    {
        if (_banked is not null && _s.Now() >= _bankedUntil)
        {
            EndBank();
        }

        if (IsEncoding)
        {
            // The counter counts down with the clock, so it reads again on every tick.
            Raise(nameof(ShowLeftHourDigits), nameof(ShowLeftMinuteDigits), nameof(ShowLeftDetail), nameof(ShowLeftName));
        }

        if (_current is null || _runSettings is null || !IsSpinning)
        {
            BitrateMeterRest();
            return;
        }

        var duration = _current.Record.DurationSeconds ?? 0;
        var output = Path.Combine(
            EpisodePipeline.WorkFolder(_runSettings.ScratchFolder, _current.Path),
            "encoded" + Path.GetExtension(_current.Path));
        // The muxer writes in bursts, so the needle reads the average so far: bytes written over
        // the stretch of the episode encoded. It settles on the output's real bitrate.
        var mediaSeconds = _progress * duration;
        if (_s.FileSize(output) is { } bytes && mediaSeconds >= MinBitrateSeconds)
        {
            BitrateMbps = Math.Clamp(bytes * 8 / 1_000_000d / mediaSeconds, 0, MaxMbps);
        }

        var now = _s.Now();
        if (_s.Gpu is { } gpu && !_loadPollBusy && now - _lastLoadPoll >= LoadPollInterval)
        {
            _lastLoadPoll = now;
            _ = PollLoadAsync(gpu);
        }

        RefreshReadouts();
    }

    private async Task PollLoadAsync(IGpuMonitor gpu)
    {
        _loadPollBusy = true;
        try
        {
            var load = await gpu.EncoderLoadAsync(CancellationToken.None);
            _s.Dispatch(() => EncoderLoad = IsSpinning ? load ?? 0 : 0);
        }
        finally
        {
            _loadPollBusy = false;
        }
    }

    private void BitrateMeterRest()
    {
        BitrateMbps = 0;
        EncoderLoad = 0;
    }

    private async Task CheckGpuAsync()
    {
        if (_s.Gpu is not { } gpu)
        {
            return;
        }

        var name = await gpu.NameAsync(CancellationToken.None);
        _s.Dispatch(() =>
        {
            _gpuName = name;
            _gpuChecked = true;
            RefreshSystemLamps();
        });
    }

    // ---- Settings ----

    private void ChooseScratch()
    {
        var folder = _s.FolderPicker?.PickFolder("Choose a local scratch folder", _settings.ScratchFolder);
        if (folder is null)
        {
            return;
        }

        _settings.ScratchFolder = folder;
        Save();
        Notice = "";
        RefreshSystemLamps();
        Raise(nameof(ScratchFolder));
        CheckScratch();
        RefreshSetupScratch();
    }

    private void StepWindow(bool start, int direction)
    {
        if (IsEncoding)
        {
            return;
        }

        if (start)
        {
            _settings.EncodeWindowStart = StepTime(_settings.EncodeWindowStart, direction);
        }
        else
        {
            _settings.EncodeWindowEnd = StepTime(_settings.EncodeWindowEnd, direction);
        }

        Save();
        RefreshWindow();
    }

    private void SetPhosphorIf(bool value, Phosphor phosphor)
    {
        if (!value || _prefs.Phosphor == phosphor)
        {
            return;
        }

        _prefs = _prefs with { Phosphor = phosphor };
        _s.ApplyTheme(_prefs.Phosphor, _prefs.Chassis);
        SavePreferences();
        Raise(nameof(Phosphor), nameof(PhosphorAmber), nameof(PhosphorGreen), nameof(PhosphorIce));
    }

    private void SetChassisIf(bool value, Chassis chassis)
    {
        if (!value || _prefs.Chassis == chassis)
        {
            return;
        }

        _prefs = _prefs with { Chassis = chassis };
        _s.ApplyTheme(_prefs.Phosphor, _prefs.Chassis);
        SavePreferences();
        Raise(nameof(Chassis), nameof(ChassisBeige), nameof(ChassisGraphite));
    }

    private void Save() => Try(() => _s.SaveSettings(_settings), "Settings could not be saved");

    private void SavePreferences() => Try(() => _s.SavePreferences(_prefs), "Deck preferences could not be saved");

    private void SaveCache() => Try(() => _s.SaveCache(_s.Cache), "The probe cache could not be saved");

    private void Try(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice = $"{what}: {ex.Message}";
        }
    }

    // ---- Refresh ----

    private void RefreshAll()
    {
        RefreshLibraryText();
        RefreshDeck();
        RefreshTransport();
        RefreshSeasonLamps();
        RefreshWindow();
        RaiseLifetime();
        RefreshCqChips();
        RefreshQueue();
        Raise(nameof(IsTapeLoaded), nameof(LoadedName), nameof(EstimateText), nameof(CanEditQuality));
        EncodeCommand.Refresh();
    }

    private void RefreshLibraryText()
    {
        Raise(nameof(MonitorMessage), nameof(LibraryTooltip));
        RefreshSetupLibrary();
    }

    private void RefreshDeck()
    {
        Raise(nameof(LabelTitle), nameof(LabelEpisode), nameof(LabelEpisodeTitle), nameof(EstimateText), nameof(Cq), nameof(CqCaption),
            nameof(SavedGigabytes), nameof(SavedDigits), nameof(SizeCaption), nameof(SizeBeforeText), nameof(SizeAfterText), nameof(AfterFraction),
            nameof(HasCqOverride), nameof(RecommendedCqIndex), nameof(LastFailureText), nameof(LastFailureDetail));
        RefreshReadouts();
    }

    private void RefreshReadouts() =>
        Raise(nameof(SourceCaption), nameof(SourceDetail), nameof(OutputCaption), nameof(OutputDetail), nameof(SourcePack),
            nameof(OutputPack), nameof(IsSpinning), nameof(EpisodeDigits), nameof(TapeTimeDigits), nameof(TapeTotalText),
            nameof(FpsDigits), nameof(PercentDigits), nameof(EpisodeDetail), nameof(EpisodeCounterName), nameof(ShowLeftHourDigits),
            nameof(ShowLeftMinuteDigits), nameof(ShowLeftDetail), nameof(ShowLeftName), nameof(DeckAutomationName), nameof(SizeCaption),
            nameof(SizeBeforeText), nameof(SizeAfterText), nameof(AfterFraction),
            nameof(EncodeLampBlink), nameof(EpisodeFraction), nameof(HeadLamp));

    /// <summary>Marks the chosen and the recommended CQ on the Quality chips.</summary>
    private void RefreshCqChips()
    {
        foreach (var chip in CqChips)
        {
            chip.Update(chip.Index == _cqIndex, chip.Index == RecommendedCqIndex);
        }
    }

    /// <summary>Rewrites every show row's tooltip, since the CQ a row estimates at can change.</summary>
    private void RefreshShowDetails()
    {
        foreach (var row in Shows)
        {
            row.Detail = DetailFor(row);
        }
    }

    private void RefreshTransport()
    {
        var held = _queue?.State is QueueState.Paused or QueueState.WaitingForWindow;
        _estimator?.SetFrozen(held, _s.Now());
        if (held)
        {
            _meter?.Interrupt();
        }
        Raise(nameof(IsEncoding), nameof(IsPaused), nameof(EncodeLamp), nameof(EncodeLampBlink), nameof(IsSpinning),
            nameof(CanEditQuality), nameof(CanEditSettings), nameof(FpsDigits), nameof(HeadLamp));
        RefreshRunLamp();
        RefreshSeasonLamps();
        EncodeCommand.Refresh();
        ToggleStopAfterCommand.Refresh();
        ChooseLibraryCommand.Refresh();
        ChooseScratchCommand.Refresh();
        RescanCommand.Refresh();
        RunSetupAgainCommand.Refresh();
        ShowTourCommand.Refresh();
        WindowStartEarlierCommand.Refresh();
        WindowStartLaterCommand.Refresh();
        WindowEndEarlierCommand.Refresh();
        WindowEndLaterCommand.Refresh();
    }

    /// <summary>
    /// The header's one lamp. While a run is going it says what the run is doing. Otherwise it
    /// says Ready, or names what is wrong: a missing tool, library or scratch folder, or
    /// recovery work that needs a look.
    /// </summary>
    private void RefreshRunLamp()
    {
        var state = _queue?.State;
        if (_isRecovering)
        {
            RunLamp.Show("Recovery check", LampState.Active, blink: true);
        }
        else if (_isClosing && _queue is not null)
        {
            RunLamp.Show(IsFinishingSwap ? "Finishing swap" : _isStopArmed ? "Last episode" : "Stopping", LampState.Active, blink: true);
        }
        else if (state == QueueState.Paused)
        {
            RunLamp.Show("Paused", LampState.Active);
        }
        else if (state == QueueState.WaitingForWindow)
        {
            RunLamp.Show("Waiting for window", LampState.Active, blink: true);
        }
        else if (_isStopArmed && _queue is not null)
        {
            RunLamp.Show("Last episode", LampState.Active, blink: true);
        }
        else if (_flow.IsCard)
        {
            RunLamp.Show("Setup", LampState.Active);
        }
        else if (_queue is not null)
        {
            RunLamp.Show(_current is null ? "Encoding" : Phase, LampState.Red, blink: true);
        }
        else if (_attention.Count > 0)
        {
            RunLamp.Show(_attention.Count == 1 ? _attention[0].Name : "Needs attention", LampState.Active, blink: true);
        }
        else
        {
            RunLamp.Show(_loaded is null ? "No show" : "Ready", _loaded is null ? LampState.Off : LampState.Ok);
        }
    }

    private void RefreshSeasonLamps()
    {
        foreach (var chip in Seasons)
        {
            var season = ListTracks.Where(t => t.Record.Season == chip.Number).ToList();
            var encoding = season.Any(t => t.Kind == TrackKind.Encoding);
            chip.Lamp = encoding ? LampState.Active : season.All(t => !t.IsQueueable) ? LampState.Ok : LampState.Off;
            chip.Blink = encoding && IsSpinning;
        }
    }

    private void RefreshSystemLamps()
    {
        var handBrakeOk = _s.ResolveTool(_settings.HandBrakeCliPath) is not null;
        var ffmpegOk = _s.ResolveTool(_settings.FfprobePath) is not null && _s.ResolveTool(_settings.FfmpegPath) is not null;
        var libraryOk = !string.IsNullOrWhiteSpace(_settings.LibraryRoot) && _s.DirectoryExists(_settings.LibraryRoot);
        var scratch = _settings.ScratchFolder;
        var root = string.IsNullOrWhiteSpace(scratch) ? null : Path.GetPathRoot(scratch);
        var scratchOk = root is not null && _s.DirectoryExists(root);

        GpuLamp.Show(_gpuName is null ? (_gpuChecked ? "NVENC · no card" : "NVENC") : "NVENC · " + _gpuName,
            _gpuName is null ? LampState.Off : LampState.Ok);
        HandBrakeLamp.State = handBrakeOk ? LampState.Ok : LampState.Off;
        FfmpegLamp.State = ffmpegOk ? LampState.Ok : LampState.Off;
        LibraryLamp.State = libraryOk ? LampState.Ok : LampState.Off;
        var drive = string.IsNullOrWhiteSpace(scratch) ? "" : root?.TrimEnd('\\') ?? "";
        ScratchLamp.Label = drive.Length is > 0 and <= 3 ? "Scratch " + drive : "Scratch";
        ScratchLamp.State = scratchOk ? LampState.Ok : LampState.Off;

        var problems = new List<(string Name, string Detail)>();
        if (!handBrakeOk)
        {
            problems.Add(("HandBrakeCLI missing", "HandBrakeCLI was not found. Install it or set its path in settings.json."));
        }

        if (!ffmpegOk)
        {
            problems.Add(("ffmpeg missing", "ffmpeg or ffprobe was not found. Install them or set their paths in settings.json."));
        }

        if (_gpuChecked && _gpuName is null)
        {
            problems.Add(("No NVENC card", "No NVIDIA card with NVENC was found, so nothing can encode."));
        }

        if (!libraryOk)
        {
            problems.Add((string.IsNullOrWhiteSpace(_settings.LibraryRoot) ? "No library" : "Library not found",
                "Choose the TV library in Setup."));
        }

        if (!scratchOk)
        {
            problems.Add((string.IsNullOrWhiteSpace(scratch) ? "No scratch folder" : "Scratch drive missing",
                "Choose a local scratch folder in Setup."));
        }

        if (_recoveryFailed)
        {
            problems.Add(("Needs attention", "Recovery could not finish. See the journal."));
        }

        if (_recoveryReviews > 0)
        {
            problems.Add(("Needs attention", $"Recovery left {_recoveryReviews} {(_recoveryReviews == 1 ? "file" : "files")} to review. See the journal."));
        }

        _attention = problems;
        Raise(nameof(AttentionDetail));
        RefreshRunLamp();
    }

    private void RaiseLifetime() =>
        Raise(nameof(LifetimeSavedBytes), nameof(LifetimeSavedDigits), nameof(LifetimeSavedUnit), nameof(LifetimeSavedName));

    private void RefreshWindow() =>
        Raise(nameof(WindowText), nameof(EncodeWindowEnabled), nameof(WindowStartText), nameof(WindowEndText));
}
