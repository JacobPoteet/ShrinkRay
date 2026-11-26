using System.Collections.ObjectModel;
using System.Globalization;
using ShrinkRay.App.Mvvm;
using ShrinkRay.Core.Library;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Setup;

namespace ShrinkRay.App.ViewModels;

/// <summary>
/// First-run setup: a self-test, three cards that ask for the library, the scratch folder and
/// what happens to originals, a three-stop tour, and one trial episode with a card after it.
/// <see cref="FirstRunFlow"/> holds the rules for moving between steps.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>How long each lamp of the self-test takes to answer: about a second for all four.</summary>
    private static readonly TimeSpan SelfTestStep = TimeSpan.FromMilliseconds(250);

    /// <summary>How long Deck ready stays up before the card moves on.</summary>
    private static readonly TimeSpan DeckReadyHold = TimeSpan.FromMilliseconds(800);

    private readonly FirstRunFlow _flow = new();
    private CancellationTokenSource? _selfTest;
    private bool _isSelfTesting;
    private bool _toolsFound;
    private bool _isSetupRerun;
    private bool _isRecycleBinChecked;
    private bool? _replaceChoice;
    private ScratchCheck? _scratchCheck;

    private bool _isTrialRun;
    private bool _isTrialJudged;
    private FirstEpisodeReport? _report;
    private bool _isFirstEpisodeCardOpen;

    /// <param name="Proven">There is a new file to play, so the trial has done its job.</param>
    private sealed record FirstEpisodeReport(
        string Heading, string Before, string After, string Text, string FilePath, bool Proven);

    /// <summary>The running self-test. Tests await it.</summary>
    public Task SetupTask { get; private set; } = Task.CompletedTask;

    public RelayCommand SetupNextCommand { get; private set; } = null!;

    public RelayCommand SetupBackCommand { get; private set; } = null!;

    /// <summary>Closes a setup that was opened again from Setup.</summary>
    public RelayCommand SetupCancelCommand { get; private set; } = null!;

    public RelayCommand CheckToolsAgainCommand { get; private set; } = null!;

    public RelayCommand ReplaceAfterChecksCommand { get; private set; } = null!;

    public RelayCommand TrialRunCommand { get; private set; } = null!;

    public RelayCommand SkipTourCommand { get; private set; } = null!;

    public RelayCommand EncodeRestCommand { get; private set; } = null!;

    public RelayCommand DoneForNowCommand { get; private set; } = null!;

    public RelayCommand RunSetupAgainCommand { get; private set; } = null!;

    public RelayCommand ShowTourCommand { get; private set; } = null!;

    // ---- Where setup stands ----

    public FirstRunStep SetupStep => _flow.Step;

    public bool IsSetupCardOpen => _flow.IsCard;

    public bool IsToolsCard => _flow.Step == FirstRunStep.Tools;

    public bool IsLibraryCard => _flow.Step == FirstRunStep.Library;

    public bool IsScratchCard => _flow.Step == FirstRunStep.Scratch;

    public bool IsOriginalsCard => _flow.Step == FirstRunStep.Originals;

    public bool IsTourLibrary => _flow.Step == FirstRunStep.TourLibrary;

    public bool IsTourEncode => _flow.Step == FirstRunStep.TourEncode;

    public bool IsTourSavings => _flow.Step == FirstRunStep.TourSavings;

    public bool CanCancelSetup => _isSetupRerun && _flow.IsCard;

    /// <summary>Setup, the tour or the first-episode card is up, so the deck's own keys wait.</summary>
    private bool IsDeckHeld => _flow.IsRunning || _isFirstEpisodeCardOpen;

    // ---- Self-test ----

    public ObservableCollection<ToolCheckViewModel> ToolChecks { get; } = [];

    public string ToolsHeading => _isSelfTesting ? "Self-test" : _toolsFound ? "Deck ready" : "Missing tools";

    public bool HasMissingTools => !_isSelfTesting && !_toolsFound;

    // ---- Library ----

    private bool LibraryFolderExists =>
        !string.IsNullOrWhiteSpace(_settings.LibraryRoot) && _s.DirectoryExists(_settings.LibraryRoot);

    private bool LibraryHasEpisodes => !_isScanning && _scans.Values.Any(s => s.Episodes.Count > 0);

    public bool IsLibraryAsking => !_isScanning && !LibraryFolderExists;

    public bool IsLibraryScanning => _isScanning;

    public bool IsLibraryFound => LibraryFolderExists && LibraryHasEpisodes;

    public bool IsLibraryEmpty => !_isScanning && LibraryFolderExists && !LibraryHasEpisodes;

    public string LibraryScanText => _scanStatus;

    public string LibraryChooseLabel => IsLibraryAsking ? "Choose" : "Choose another";

    private long ProjectedSavings => _scans.Values.Sum(s =>
        ShowSummary.From(s, Classify).ProjectedSavingsAt(RatioFor(s.Name, _settings.CqForShow(s.Name))));

    /// <summary>The space the library would give back, for the card's big digits.</summary>
    public string PayoffDigits => Format.WholeGigabytes(IsLibraryFound ? ProjectedSavings : 0);

    /// <summary>Such as <c>7 shows · 878 GB · about 570 GB to free at CQ 28</c>.</summary>
    public string PayoffText
    {
        get
        {
            if (!IsLibraryFound)
            {
                return "";
            }

            var shows = _scans.Values.Where(s => s.Episodes.Count > 0).ToList();
            var total = shows.Sum(s => s.Episodes.Sum(e => e.SizeBytes));
            var savings = ProjectedSavings;
            var count = shows.Count == 1 ? "1 show" : $"{shows.Count} shows";
            var free = savings > 0
                ? $"about {Format.WholeGigabytes(savings)} GB to free at CQ {_settings.Cq}"
                : "nothing left to shrink";
            return $"{count} · {Format.WholeGigabytes(total)} GB · {free}";
        }
    }

    /// <summary>A library on a share: replaced originals go to the share's recycle bin, if it has one.</summary>
    public bool ShowsRecycleReminder => IsLibraryFound && _s.IsNetworkPath(_settings.LibraryRoot);

    public bool IsRecycleBinChecked
    {
        get => _isRecycleBinChecked;
        set => Set(ref _isRecycleBinChecked, value);
    }

    // ---- Scratch ----

    public string ScratchFreeText => _scratchCheck?.FreeBytes is { } free
        ? $"{Format.WholeGigabytes(free)} GB free on {Path.GetPathRoot(_settings.ScratchFolder)?.TrimEnd('\\')}"
        : "Free space unknown";

    /// <summary>What is wrong with the scratch folder. Empty when nothing is.</summary>
    public string ScratchWarning
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_settings.ScratchFolder))
            {
                return "Choose a scratch folder.";
            }

            if (!ScratchDriveExists)
            {
                return "That drive is missing. Choose another folder.";
            }

            return _scratchCheck switch
            {
                { IsInLibrary: true } => "That folder is inside the library. Choose one outside it.",
                { IsOnNetwork: true } => "That folder is on a share. Choose a local disk.",
                { IsLowOnSpace: true } c =>
                    $"Low on space. Allow {Format.Gigabytes(c.NeededBytes)} GB, twice your largest episode.",
                _ => "",
            };
        }
    }

    private bool ScratchDriveExists =>
        Path.GetPathRoot(_settings.ScratchFolder) is { Length: > 0 } root && _s.DirectoryExists(root);

    private bool IsScratchUsable => ScratchDriveExists && _scratchCheck is { IsUsable: true };

    private void CheckScratch()
    {
        var folder = _settings.ScratchFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            _scratchCheck = null;
            return;
        }

        var largest = _scans.Values.SelectMany(s => s.Episodes).Select(e => e.SizeBytes).DefaultIfEmpty(0).Max();
        _scratchCheck = ScratchCheck.For(folder, _settings.LibraryRoot, largest, _s.FreeSpace(folder), _s.IsNetworkPath(folder));
    }

    // ---- Tour ----

    public string TourEncodeText => FirstRunFlow.IsFirstEpisodePending(_settings)
        ? "Press Encode. Your first run stops after one episode."
        : "Press Encode to shrink the show in the deck.";

    // ---- First episode ----

    public bool IsFirstEpisodeCardOpen => _isFirstEpisodeCardOpen;

    public string FirstEpisodeHeading => _report?.Heading ?? "";

    /// <summary>The episode and its old size, such as <c>S01E01: 1.80 GB</c>. The card draws an arrow after it.</summary>
    public string FirstEpisodeBefore => _report?.Before ?? "";

    public string FirstEpisodeAfter => _report?.After ?? "";

    public bool HasFirstEpisodeSizes => FirstEpisodeAfter.Length > 0;

    public string FirstEpisodeText => _report?.Text ?? "";

    /// <summary>In trial mode, where the new file sits in scratch. Empty otherwise.</summary>
    public string FirstEpisodeFile => _report?.FilePath ?? "";

    public bool CanEncodeRest => _report is { Proven: true } && _tracks.Any(t => t.IsQueueable);

    public string FirstEpisodeCloseLabel => _report is { Proven: true } ? "Done for now" : "OK";

    // ---- Commands ----

    private void CreateFirstRunCommands()
    {
        SetupNextCommand = new RelayCommand(Advance, () => _flow.IsRunning && FirstRunFlow.CanAdvance(_flow.Step, Facts()));
        SetupBackCommand = new RelayCommand(() => Move(_flow.Back()), () => _flow.CanGoBack);
        SetupCancelCommand = new RelayCommand(CancelSetup, () => CanCancelSetup);
        CheckToolsAgainCommand = new RelayCommand(() => SetupTask = RunSelfTestAsync(), () => IsToolsCard && !_isSelfTesting);
        ReplaceAfterChecksCommand = new RelayCommand(() => ChooseOriginals(replace: true), () => IsOriginalsCard);
        TrialRunCommand = new RelayCommand(() => ChooseOriginals(replace: false), () => IsOriginalsCard);
        SkipTourCommand = new RelayCommand(() => Move(_flow.Skip()), () => _flow.IsTour);
        EncodeRestCommand = new RelayCommand(EncodeRest, () => _isFirstEpisodeCardOpen && CanEncodeRest);
        DoneForNowCommand = new RelayCommand(() => CloseFirstEpisodeCard(), () => _isFirstEpisodeCardOpen);
        RunSetupAgainCommand = new RelayCommand(RunSetupAgain, () => !IsEncoding && !_isRecovering && !IsDeckHeld);
        ShowTourCommand = new RelayCommand(ShowTour, () => !IsEncoding && !_isRecovering && !IsDeckHeld);

        AddToolCheck("HandBrakeCLI", "HandBrakeCLI.exe", ToolInstall.HandBrakeCli);
        AddToolCheck("ffprobe", "ffprobe.exe", ToolInstall.Ffmpeg);
        AddToolCheck("ffmpeg", "ffmpeg.exe", ToolInstall.Ffmpeg);
        AddToolCheck("NVENC", null, null, "Needs an NVIDIA card with NVENC and its driver. Install the driver, then check again.");

        void AddToolCheck(string name, string? fileName, string? command, string note = "") =>
            ToolChecks.Add(new ToolCheckViewModel(name, fileName, command, note, CopyInstallCommand, LocateTool));
    }

    private FirstRunFacts Facts() => new(_toolsFound, IsLibraryFound, IsScratchUsable, _replaceChoice);

    private void Advance() => Move(_flow.Next(Facts()));

    private void Move(bool moved)
    {
        if (!moved)
        {
            return;
        }

        if (IsScratchCard)
        {
            CheckScratch();
        }

        RefreshSetup();
    }

    /// <summary>Opens the cards at the self-test. Nothing counts as set up until the trial is answered.</summary>
    private void BeginSetup()
    {
        _settings.FirstRunCompleted = false;
        _settings.FirstEpisodePending = false;
        Save();
        _replaceChoice = null;
        IsRecycleBinChecked = false;
        _flow.Begin();
        SetupTask = RunSelfTestAsync();
    }

    private void RunSetupAgain()
    {
        IsSettingsOpen = false;
        _isSetupRerun = true;
        BeginSetup();
    }

    private void CancelSetup()
    {
        _selfTest?.Cancel();
        _flow.Cancel();
        CompleteFirstRun();
        RefreshSetup();
    }

    private void ShowTour()
    {
        IsSettingsOpen = false;
        _flow.BeginTour();
        RefreshSetup();
    }

    private async Task RunSelfTestAsync()
    {
        _selfTest?.Cancel();
        var cts = new CancellationTokenSource();
        _selfTest = cts;
        var token = cts.Token;
        var delay = _s.Delay ?? Task.Delay;

        _isSelfTesting = true;
        _toolsFound = false;
        foreach (var check in ToolChecks)
        {
            check.Reset();
        }

        RefreshSetup();
        try
        {
            var paths = new[] { _settings.HandBrakeCliPath, _settings.FfprobePath, _settings.FfmpegPath };
            for (var i = 0; i < paths.Length; i++)
            {
                await delay(SelfTestStep, token);
                token.ThrowIfCancellationRequested();
                if (_s.ResolveTool(paths[i]) is not null)
                {
                    ToolChecks[i].Found();
                }
                else
                {
                    ToolChecks[i].Missing();
                }
            }

            await delay(SelfTestStep, token);
            var card = _s.Gpu is { } gpu ? await gpu.NameAsync(token) : null;
            token.ThrowIfCancellationRequested();
            _gpuName = card;
            _gpuChecked = true;
            if (card is null)
            {
                ToolChecks[^1].Missing();
            }
            else
            {
                ToolChecks[^1].Found(card);
            }

            _isSelfTesting = false;
            _toolsFound = ToolChecks.All(c => !c.IsMissing);
            RefreshSystemLamps();
            RefreshSetup();
            if (!_toolsFound)
            {
                return;
            }

            await delay(DeckReadyHold, token);
            token.ThrowIfCancellationRequested();
            if (IsToolsCard)
            {
                Advance();
                await ScanLibraryAsync(_prefs.LastShow);
            }
        }
        catch (OperationCanceledException)
        {
            // Check again started a newer test, or setup closed.
        }
    }

    private void CopyInstallCommand(ToolCheckViewModel check)
    {
        _s.CopyText(check.Command);
        check.MarkCopied();
    }

    private void LocateTool(ToolCheckViewModel check)
    {
        if (check.FileName is null || _s.FolderPicker?.PickProgram($"Locate {check.FileName}", check.FileName) is not { } file)
        {
            return;
        }

        switch (ToolChecks.IndexOf(check))
        {
            case 0:
                _settings.HandBrakeCliPath = file;
                break;
            case 1:
                _settings.FfprobePath = file;
                break;
            default:
                _settings.FfmpegPath = file;
                break;
        }

        Save();
        SetupTask = RunSelfTestAsync();
    }

    private void ChooseOriginals(bool replace)
    {
        _replaceChoice = replace;
        _settings.ReplaceOriginals = replace;

        // The cards are done. A start from here goes to the deck with the trial still to run.
        _settings.FirstEpisodePending = true;
        Save();
        Raise(nameof(ReplaceOriginals));
        Reclassify();
        Advance();

        // The smallest show goes in the deck, so the first test is short.
        if (Shows.OrderBy(r => r.Summary.TotalBytes).FirstOrDefault() is { } smallest)
        {
            Select(smallest.Scan);
        }
    }

    private void CompleteFirstRun()
    {
        _settings.FirstRunCompleted = true;
        _settings.FirstEpisodePending = false;
        Save();
        Raise(nameof(TourEncodeText));
    }

    // ---- The trial episode ----

    /// <summary>
    /// Reads the trial's first episode as it ends. With the stop still armed the card waits for
    /// the run to finish. A user who took the stop back chose to carry on, so no card shows.
    /// </summary>
    private void JudgeTrial(TrackViewModel? track, EpisodeFinished finished)
    {
        if (!_isTrialRun || _isTrialJudged)
        {
            return;
        }

        _isTrialJudged = true;
        if (finished.End != EpisodeEnd.Finished || finished.Result is not { } result || track is null)
        {
            // Skipped or stopped from the deck. The next run is the trial again.
            return;
        }

        var report = Report(track, result);
        if (_isStopArmed)
        {
            _report = report;
        }
        else if (report.Proven)
        {
            CompleteFirstRun();
        }
    }

    private FirstEpisodeReport Report(TrackViewModel track, PipelineResult result)
    {
        var before = string.Create(CultureInfo.InvariantCulture, $"{track.Code}: {Format.Gigabytes(track.OriginalBytes)} GB");
        string After(long? bytes) => bytes is { } b ? $"{Format.Gigabytes(b)} GB" : "";

        return result switch
        {
            { Outcome: PipelineOutcome.Replaced } => new("First episode done", before, After(track.NewBytes),
                "Open it in Plex and check it plays.", "", Proven: true),
            { Outcome: PipelineOutcome.KeptOriginal, KeptEncodePath: { } file } => new("First episode done", before, After(_s.FileSize(file)),
                "Your original is untouched. Play the new file from scratch to check it.", file, Proven: true),
            { Outcome: PipelineOutcome.KeptOriginal } => new("Original kept", "", "",
                $"{track.Code} saved too little, so the original stays. The next run tries another episode and stops again. {result.Message}",
                "", Proven: false),
            { Outcome: PipelineOutcome.SourceChanged } => new("File changed", "", "",
                $"{track.Code}: {result.Message} It stops after one episode again.", "", Proven: false),
            _ => new("First episode failed", "", "",
                $"{track.Code}: {result.Message.TrimEnd('.')}. The next run tries again and stops after one episode.", "", Proven: false),
        };
    }

    /// <summary>The trial run is over. Its card goes up when there is one to show.</summary>
    private void FinishTrial()
    {
        _isTrialRun = false;
        if (_report is null || _isClosing)
        {
            return;
        }

        _isFirstEpisodeCardOpen = true;
        RefreshSetup();
    }

    private void EncodeRest()
    {
        CloseFirstEpisodeCard();
        Encode();
    }

    private void CloseFirstEpisodeCard()
    {
        if (_report is { Proven: true })
        {
            CompleteFirstRun();
        }

        _isFirstEpisodeCardOpen = false;
        _report = null;
        RefreshSetup();
    }

    private void RefreshSetup()
    {
        Raise(nameof(SetupStep), nameof(IsSetupCardOpen), nameof(IsToolsCard), nameof(IsLibraryCard), nameof(IsScratchCard),
            nameof(IsOriginalsCard), nameof(IsTourLibrary), nameof(IsTourEncode), nameof(IsTourSavings), nameof(CanCancelSetup),
            nameof(ToolsHeading), nameof(HasMissingTools), nameof(TourEncodeText), nameof(MonitorMessage));
        Raise(nameof(IsFirstEpisodeCardOpen), nameof(FirstEpisodeHeading), nameof(FirstEpisodeBefore), nameof(FirstEpisodeAfter),
            nameof(HasFirstEpisodeSizes), nameof(FirstEpisodeText), nameof(FirstEpisodeFile), nameof(CanEncodeRest),
            nameof(FirstEpisodeCloseLabel));
        RefreshSetupLibrary();
        RefreshSetupScratch();
        SetupBackCommand.Refresh();
        SetupCancelCommand.Refresh();
        CheckToolsAgainCommand.Refresh();
        ReplaceAfterChecksCommand.Refresh();
        TrialRunCommand.Refresh();
        SkipTourCommand.Refresh();
        EncodeRestCommand.Refresh();
        DoneForNowCommand.Refresh();
        DismissCardCommand.Refresh();
        OpenSettingsCommand.Refresh();
        RefreshTransport();
    }

    private void RefreshSetupLibrary()
    {
        if (IsLibraryCard)
        {
            Raise(nameof(IsLibraryAsking), nameof(IsLibraryScanning), nameof(IsLibraryFound), nameof(IsLibraryEmpty),
                nameof(LibraryScanText), nameof(LibraryChooseLabel), nameof(PayoffDigits), nameof(PayoffText), nameof(ShowsRecycleReminder));
        }

        SetupNextCommand.Refresh();
    }

    private void RefreshSetupScratch()
    {
        if (IsScratchCard)
        {
            Raise(nameof(ScratchFreeText), nameof(ScratchWarning));
        }

        SetupNextCommand.Refresh();
    }
}
