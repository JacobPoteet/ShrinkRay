using ShrinkRay.App.Services;
using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Library;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Swap;
using ShrinkRay.Theme;
using ShrinkRay.Theme.Controls;

namespace ShrinkRay.App.ViewModels;

public sealed partial class MainViewModel
{
    // ---- Startup ----

    /// <summary>
    /// Runs journal recovery first, then scans the library and reloads the last tape. Encode
    /// stays off until recovery has finished.
    /// </summary>
    public async Task StartAsync()
    {
        _s.ApplyTheme(_prefs.Phosphor, _prefs.Chassis);
        if (string.IsNullOrWhiteSpace(_settings.ScratchFolder) && !string.IsNullOrWhiteSpace(_defaultScratch))
        {
            _settings.ScratchFolder = _defaultScratch;
            Save();
        }

        RefreshSystemLamps();
        _ = CheckGpuAsync();

        SetRecovering(true);
        try
        {
            var actions = await _s.Recover(SettingsStore.Normalize(_settings), CancellationToken.None);
            RecoveryText = DescribeRecovery(actions);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            RecoveryText = "RECOVERY FAILED. SEE THE JOURNAL";
            Notice = "Recovery could not finish: " + ex.Message;
        }
        finally
        {
            SetRecovering(false);
        }

        await ScanLibraryAsync(_prefs.LastShow);
    }

    public static string DescribeRecovery(IReadOnlyList<RecoveryAction> actions)
    {
        if (actions.Count == 0)
        {
            return "RECOVERY: ALL CLEAR";
        }

        var review = actions.Count(a => a.Kind == RecoveryKind.NeedsReview);
        var fixedCount = actions.Count - review;
        var parts = new List<string>();
        if (fixedCount > 0)
        {
            parts.Add($"{fixedCount} FIXED");
        }

        if (review > 0)
        {
            parts.Add($"{review} TO REVIEW");
        }

        return "RECOVERY: " + string.Join(" · ", parts);
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
                    SelectedShow = Shows.FirstOrDefault(r => string.Equals(r.FolderName, scan.Name, StringComparison.OrdinalIgnoreCase));
                    LoadShow(scan);
                }
            }

            SaveCache();
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

        SelectedShow ??= Shows.FirstOrDefault(r => r.IsLoaded) ?? Shows.FirstOrDefault();
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
            }
            else
            {
                var row = new ShowRowViewModel(scan, summary)
                {
                    IsLoaded = string.Equals(scan.Name, _loaded?.Name, StringComparison.OrdinalIgnoreCase),
                };
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

    private EpisodeStatus Classify(EpisodeRecord episode) =>
        EpisodeStatusRules.Classify(episode, _s.Journal.CurrentState(episode.Path), _s.Ledger.Get(episode.Path) is not null);

    private void ChooseLibrary()
    {
        var folder = _s.FolderPicker?.PickFolder("Choose the TV library folder", _settings.LibraryRoot);
        if (folder is not null)
        {
            SetLibraryRoot(folder, null);
        }
    }

    private void SetLibraryRoot(string folder, string? loadShow)
    {
        _settings.LibraryRoot = folder.TrimEnd('\\', '/');
        if (_settings.LibraryRoot.EndsWith(':'))
        {
            _settings.LibraryRoot += "\\";
        }

        Save();
        Raise(nameof(LibraryRoot), nameof(LibraryLabel));
        _ = ScanLibraryAsync(loadShow ?? _loaded?.Name);
    }

    /// <summary>
    /// Handles a folder dropped on the window. A show folder (season folders or episodes inside)
    /// loads as a tape, making its parent the library root; any other folder becomes the root.
    /// </summary>
    /// <returns>False when the drop was refused, such as during an encode.</returns>
    public bool DropFolder(string path)
    {
        if (IsEncoding || !_s.DirectoryExists(path))
        {
            return false;
        }

        var folder = path.TrimEnd('\\', '/');
        if (EpisodeName.SeasonFromFolder(Path.GetFileName(folder)) is not null && Path.GetDirectoryName(folder) is { } showFolder)
        {
            folder = showFolder;
        }

        var looksLikeShow = _s.Disk.EnumerateFiles(folder).Any(f => LibraryScanner.VideoExtensions.Contains(Path.GetExtension(f)))
            || _s.Disk.EnumerateDirectories(folder).Any(d => EpisodeName.SeasonFromFolder(Path.GetFileName(d)) is not null);
        var parent = Path.GetDirectoryName(folder);
        if (!looksLikeShow || parent is null)
        {
            SetLibraryRoot(folder, null);
            return true;
        }

        var name = Path.GetFileName(folder);
        if (string.Equals(parent.TrimEnd('\\'), _settings.LibraryRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            && _scans.TryGetValue(name, out var scan))
        {
            SelectedShow = Shows.FirstOrDefault(r => string.Equals(r.FolderName, name, StringComparison.OrdinalIgnoreCase));
            LoadShow(scan);
            return true;
        }

        SetLibraryRoot(parent, name);
        return true;
    }

    // ---- Tape ----

    private void LoadTape()
    {
        if (_selectedShow is { } row && !IsEncoding)
        {
            LoadShow(row.Scan);
        }
    }

    private void LoadShow(ShowScan scan)
    {
        _loaded = scan;
        foreach (var row in Shows)
        {
            row.IsLoaded = string.Equals(row.FolderName, scan.Name, StringComparison.OrdinalIgnoreCase);
        }

        _tracks.Clear();
        foreach (var episode in scan.Episodes)
        {
            _tracks.Add(TrackViewModel.From(episode, Classify(episode), _s.Ledger.Get(episode.Path), _s.Journal.Latest(episode.Path)?.Reason));
        }

        Seasons.Clear();
        foreach (var season in _tracks.Select(t => t.Record.Season).Distinct().OrderBy(s => s == 0 ? int.MaxValue : s))
        {
            Seasons.Add(new SeasonChipViewModel(season, SelectSeason));
        }

        var first = _tracks.FirstOrDefault(t => t.IsQueueable)?.Record.Season ?? Seasons.FirstOrDefault()?.Number ?? 0;
        ShowSeason(first);

        _cqIndex = NearestDetent(_settings.CqForShow(scan.Name));
        Raise(nameof(CqIndex), nameof(Cq));
        _prefs = _prefs with { LastShow = scan.Name };
        SavePreferences();
        Notice = "";
        RefreshAll();
    }

    private void Eject()
    {
        if (IsEncoding)
        {
            return;
        }

        _loaded = null;
        _tracks.Clear();
        Tracks.Clear();
        Seasons.Clear();
        foreach (var row in Shows)
        {
            row.IsLoaded = false;
        }

        _prefs = _prefs with { LastShow = null };
        SavePreferences();
        RefreshAll();
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
        foreach (var track in _tracks.Where(t => t.Record.Season == season))
        {
            Tracks.Add(track);
        }

        RefreshSeasonLamps();
        Raise(nameof(SelectedSeason), nameof(JCardHeader), nameof(SeasonFooter));
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

        if (_loaded is null || _isRecovering)
        {
            RefreshTransport();
            return;
        }

        var tracks = _tracks.Where(t => t.IsQueueable).ToList();
        if (tracks.Count == 0)
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

        var snapshot = SettingsStore.Normalize(_settings);
        _runSettings = snapshot;
        _runCq = snapshot.CqForShow(_loaded.Name);
        Notice = "";

        var queue = new EncodeQueue(
            _s.CreateRunner(snapshot),
            _s.Gate,
            () => snapshot.IsInsideEncodeWindow(TimeOnly.FromDateTime(_s.Now())),
            _s.Delay);
        queue.EpisodeStarted += path => _s.Dispatch(() => OnEpisodeStarted(path));
        queue.Progress += (path, p) => _s.Dispatch(() => OnProgress(path, p));
        queue.EpisodeFinished += f => _s.Dispatch(() => OnEpisodeFinished(f));
        queue.StateChanged += _ => _s.Dispatch(RefreshTransport);
        _queue = queue;
        RefreshTransport();

        CurrentRun = RunQueueAsync(queue, tracks.Select(t => t.Path).ToList());
    }

    private async Task RunQueueAsync(EncodeQueue queue, IReadOnlyList<string> paths)
    {
        try
        {
            await queue.RunAsync(paths).ConfigureAwait(false);
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

    private void Stop()
    {
        _queue?.Stop();
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
        _progress = 0;
        _fps = null;
        _lastOutputBytes = null;
        _lastOutputProgress = 0;
        if (_current is null)
        {
            return;
        }

        _current.SetEncoding(0, Ratio);
        if (_current.Record.Season != _selectedSeason)
        {
            ShowSeason(_current.Record.Season);
        }

        RefreshDeck();
        RefreshTransport();
    }

    private void OnProgress(string path, EncodeProgress progress)
    {
        if (_current is null || !string.Equals(_current.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _progress = Math.Clamp(progress.Percent / 100, 0, 1);
        _fps = progress.Fps ?? progress.AverageFps;
        _current.SetEncoding(_progress, Ratio);
        RefreshReadouts();
    }

    private void OnEpisodeFinished(EpisodeFinished finished)
    {
        var track = Find(finished.Path);
        if (track is not null)
        {
            switch (finished.End)
            {
                case EpisodeEnd.Skipped:
                    track.SetSkipped("SKIPPED", "Skipped from the deck. The original is untouched and the next run tries it again.");
                    break;
                case EpisodeEnd.Stopped:
                    track.SetQueued();
                    break;
                default:
                    ApplyResult(track, finished.Result!);
                    break;
            }
        }

        if (_current == track)
        {
            _current = null;
            _progress = 0;
            _fps = null;
        }

        RefreshAll();
    }

    private void ApplyResult(TrackViewModel track, PipelineResult result)
    {
        switch (result.Outcome)
        {
            case PipelineOutcome.Replaced:
                var newBytes = _s.FileSize(track.Path) ?? (long)(track.OriginalBytes * Ratio);
                _s.Ledger.Record(new LedgerEntry(track.Path, track.OriginalBytes, newBytes, _runCq, DateTimeOffset.Now));
                track.SetShrunk(newBytes);
                break;
            case PipelineOutcome.KeptOriginal:
                track.SetKept(result.Verification?.Savings, result.Message);
                break;
            case PipelineOutcome.Skipped:
                track.SetSkipped("SKIP", result.Message);
                break;
            default:
                track.SetFailed(result.Message);
                break;
        }
    }

    private void OnRunFinished()
    {
        _queue = null;
        _current = null;
        _progress = 0;
        _fps = null;
        BitrateMeterRest();
        RefreshAll();

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
            AddScan(scan);
            SaveCache();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next full scan picks it up.
        }
    }

    // ---- Instruments ----

    /// <summary>
    /// Called twice a second by the window. Reads the output file's growth for the bitrate
    /// needle and polls the encoder load, and keeps the encode window clock current.
    /// </summary>
    public void Tick()
    {
        RefreshWindow(force: false);
        if (_current is null || _runSettings is null || !IsSpinning)
        {
            BitrateMeterRest();
            return;
        }

        var duration = _current.Record.DurationSeconds ?? 0;
        var output = Path.Combine(
            EpisodePipeline.WorkFolder(_runSettings.ScratchFolder, _current.Path),
            "encoded" + Path.GetExtension(_current.Path));
        if (_s.FileSize(output) is { } bytes && duration > 0)
        {
            var mediaSeconds = (_progress - _lastOutputProgress) * duration;
            if (_lastOutputBytes is { } last && mediaSeconds >= 1 && bytes >= last)
            {
                var mbps = (bytes - last) * 8 / 1_000_000d / mediaSeconds;
                BitrateMbps = Math.Clamp(_bitrate <= 0 ? mbps : (_bitrate * 0.5) + (mbps * 0.5), 0, MaxMbps);
                _lastOutputBytes = bytes;
                _lastOutputProgress = _progress;
            }
            else if (_lastOutputBytes is null)
            {
                _lastOutputBytes = bytes;
                _lastOutputProgress = _progress;
            }
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
        RefreshRuleLamps();
        RefreshFooterLamps();
        RefreshWindow(force: true);
        Raise(nameof(IsTapeLoaded), nameof(LoadedName), nameof(JCardHeader), nameof(SeasonFooter),
            nameof(LoadLampState), nameof(LoadLampBlink), nameof(EstimateText), nameof(IsKnobEnabled));
        EjectCommand.Refresh();
        EncodeCommand.Refresh();
    }

    private void RefreshLibraryText() =>
        Raise(nameof(ShowCountText), nameof(HiddenText), nameof(MonitorMessage), nameof(LibraryLabel), nameof(CursorInfo));

    private void RefreshDeck()
    {
        Raise(nameof(LabelTitle), nameof(LabelSide), nameof(LabelEpisode), nameof(EstimateText), nameof(Cq),
            nameof(FooterTitle), nameof(SavedGigabytes), nameof(SavedDigits), nameof(BeforeText), nameof(AfterText),
            nameof(AfterFraction), nameof(EpisodesDoneText), nameof(HandledFraction), nameof(SeasonFooter));
        RefreshReadouts();
    }

    private void RefreshReadouts() =>
        Raise(nameof(SourceText), nameof(OutputText), nameof(SourcePack), nameof(OutputPack), nameof(IsSpinning),
            nameof(EpisodeDigits), nameof(TapeTimeDigits), nameof(TapeTotalText), nameof(FpsDigits), nameof(PercentDigits),
            nameof(DeckAutomationName), nameof(EpisodesDoneText), nameof(AfterText), nameof(AfterFraction), nameof(EncodeLampBlink));

    private void RefreshTransport()
    {
        Raise(nameof(IsEncoding), nameof(IsPaused), nameof(EncodeLamp), nameof(EncodeLampBlink), nameof(IsSpinning),
            nameof(IsKnobEnabled), nameof(CanEditSettings), nameof(FpsDigits));

        var state = _queue?.State;
        if (_isRecovering)
        {
            RunLamp.Show("Recovery check", LampState.Active, blink: true);
        }
        else if (state == QueueState.Paused)
        {
            RunLamp.Show("Paused", LampState.Active);
        }
        else if (state == QueueState.WaitingForWindow)
        {
            RunLamp.Show("Waiting for window", LampState.Active, blink: true);
        }
        else if (_queue is not null)
        {
            RunLamp.Show(_current is null ? "Encoding" : "Encoding " + _current.Code, LampState.Red, blink: true);
        }
        else
        {
            RunLamp.Show(_loaded is null ? "No tape" : "Ready", _loaded is null ? LampState.Off : LampState.Ok);
        }

        LibraryLamp.Blink = _queue is not null && state == QueueState.Running;
        RefreshSeasonLamps();
        LoadTapeCommand.Refresh();
        EncodeCommand.Refresh();
        EjectCommand.Refresh();
        ChooseLibraryCommand.Refresh();
        ChooseScratchCommand.Refresh();
        RescanCommand.Refresh();
    }

    private void RefreshSeasonLamps()
    {
        foreach (var chip in Seasons)
        {
            var season = _tracks.Where(t => t.Record.Season == chip.Number).ToList();
            var encoding = season.Any(t => t.Kind == TrackKind.Encoding);
            chip.Lamp = encoding ? LampState.Active : season.All(t => !t.IsQueueable) ? LampState.Ok : LampState.Off;
            chip.Blink = encoding && IsSpinning;
        }
    }

    private void RefreshRuleLamps()
    {
        KeepLamp.Label = $"Keep under {Format.Percent(_settings.MinimumSavings)}";
        FontsLamp.State = _settings.RestoreFonts ? LampState.Ok : LampState.Off;
    }

    private void RefreshFooterLamps()
    {
        if (_settings.ReplaceOriginals)
        {
            OriginalsLamp.Show("Replaced after checks pass", LampState.Ok);
            RecycleLamp.Show("Old file to NAS recycle bin", LampState.Ok);
        }
        else
        {
            OriginalsLamp.Show("Originals kept", LampState.Off);
            RecycleLamp.Show("Encodes stay in scratch", LampState.Off);
        }

        Raise(nameof(ReplaceOriginals));
    }

    private void RefreshSystemLamps()
    {
        GpuLamp.Show(_gpuName is null ? (_gpuChecked ? "NVENC · no card" : "NVENC") : "NVENC · " + _gpuName,
            _gpuName is null ? LampState.Off : LampState.Ok);
        HandBrakeLamp.State = _s.ResolveTool(_settings.HandBrakeCliPath) is null ? LampState.Off : LampState.Ok;
        FfmpegLamp.State = _s.ResolveTool(_settings.FfprobePath) is null || _s.ResolveTool(_settings.FfmpegPath) is null
            ? LampState.Off
            : LampState.Ok;

        var libraryOk = !string.IsNullOrWhiteSpace(_settings.LibraryRoot) && _s.DirectoryExists(_settings.LibraryRoot);
        LibraryLamp.State = libraryOk ? LampState.Ok : LampState.Off;

        var scratch = _settings.ScratchFolder;
        var drive = string.IsNullOrWhiteSpace(scratch) ? "" : Path.GetPathRoot(scratch)?.TrimEnd('\\') ?? "";
        ScratchLamp.Label = drive.Length is > 0 and <= 3 ? "Scratch " + drive : "Scratch";
        var root = string.IsNullOrWhiteSpace(scratch) ? null : Path.GetPathRoot(scratch);
        ScratchLamp.State = root is not null && _s.DirectoryExists(root) ? LampState.Ok : LampState.Off;
    }

    private void RefreshWindow(bool force)
    {
        var now = _s.Now();
        var minute = (now.Hour * 60) + now.Minute;
        if (!force && minute == _lastMinute)
        {
            return;
        }

        _lastMinute = minute;
        var clock = now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (!_settings.EncodeWindowEnabled)
        {
            WindowLamp.Show("Off · now " + clock, LampState.Off);
        }
        else if (_settings.IsInsideEncodeWindow(TimeOnly.FromDateTime(now)))
        {
            WindowLamp.Show("Active · now " + clock, LampState.Active);
        }
        else
        {
            WindowLamp.Show("Closed · now " + clock, LampState.Off);
        }

        Raise(nameof(WindowEnabled), nameof(WindowText), nameof(WindowDymo), nameof(NowHours),
            nameof(WindowStartHours), nameof(WindowEndHours), nameof(EncodeWindowEnabled));
    }
}
