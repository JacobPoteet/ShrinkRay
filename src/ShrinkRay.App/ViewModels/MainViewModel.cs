using System.Collections.ObjectModel;
using System.Globalization;
using ShrinkRay.App.Mvvm;
using ShrinkRay.App.Services;
using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Library;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Swap;
using ShrinkRay.Theme;
using ShrinkRay.Theme.Controls;

namespace ShrinkRay.App.ViewModels;

/// <summary>
/// The main deck: monitor A lists candidate shows, double-click or Enter puts one in the deck, and Encode
/// runs its eligible episodes through the engine one at a time.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>How far one press of an encode window key moves its time.</summary>
    public const int WindowStepMinutes = 30;

    /// <summary>The quality knob's detents.</summary>
    public static readonly IReadOnlyList<int> CqDetents = [20, 22, 24, 26, 28, 30];

    /// <summary>Top of the bitrate meter's scale.</summary>
    public const double MaxMbps = 8;

    private static readonly TimeSpan LoadPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Seconds of the episode encoded before the bitrate needle reads; earlier is mostly header.</summary>
    private const double MinBitrateSeconds = 5;

    private readonly DeckServices _s;
    private readonly ShrinkRaySettings _settings;
    private readonly LibraryScanner _scanner;
    private readonly Dictionary<string, ShowScan> _scans = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HiddenReason> _hidden = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TrackViewModel> _tracks = [];
    private readonly string? _defaultScratch;

    private DeckPreferences _prefs;
    private CancellationTokenSource? _scanCts;
    private ShowRowViewModel? _selectedShow;
    private ShowScan? _loaded;
    private int _selectedSeason;
    private int _cqIndex;

    private EncodeQueue? _queue;
    private ShrinkRaySettings? _runSettings;
    private int _runCq;
    private TrackViewModel? _current;
    private double _progress;

    /// <summary>Bytes moved during the copy or the upload, 0 to 1. Null in every other phase.</summary>
    private double? _transfer;
    private double? _fps;
    private EpisodePhase _phase;

    private double _bitrate;
    private double _encoderLoad;
    private DateTime _lastLoadPoll = DateTime.MinValue;
    private bool _loadPollBusy;

    private bool _isRecovering;
    private bool _isScanning;
    private string _scanStatus = "";
    private int _recoveryReviews;
    private bool _recoveryFailed;
    private List<(string Name, string Detail)> _attention = [];
    private string _notice = "";
    private bool _isSettingsOpen;
    private string? _gpuName;
    private bool _gpuChecked;

    /// <param name="defaultScratch">The scratch folder to use when settings name none.</param>
    public MainViewModel(DeckServices services, ShrinkRaySettings settings, DeckPreferences preferences, string? defaultScratch = null)
    {
        _s = services;
        _settings = settings;
        _prefs = preferences;
        _defaultScratch = defaultScratch;
        _scanner = new LibraryScanner(services.Disk, services.Probe, services.Cache);

        EncodeCommand = new RelayCommand(Encode, () => (_loaded is not null && !_isRecovering) || IsEncoding);
        PauseCommand = new RelayCommand(TogglePause);
        StopCommand = new RelayCommand(Stop);
        SkipCommand = new RelayCommand(Skip);
        ChooseLibraryCommand = new RelayCommand(ChooseLibrary, () => !IsEncoding);
        ChooseScratchCommand = new RelayCommand(ChooseScratch, () => !IsEncoding);
        OpenSettingsCommand = new RelayCommand(() => IsSettingsOpen = true);
        CloseSettingsCommand = new RelayCommand(() => IsSettingsOpen = false);
        RescanCommand = new RelayCommand(() => _ = ScanLibraryAsync(_loaded?.Name), () => !IsEncoding && !_isRecovering);
        WindowStartEarlierCommand = new RelayCommand(() => StepWindow(start: true, -1), () => !IsEncoding);
        WindowStartLaterCommand = new RelayCommand(() => StepWindow(start: true, 1), () => !IsEncoding);
        WindowEndEarlierCommand = new RelayCommand(() => StepWindow(start: false, -1), () => !IsEncoding);
        WindowEndLaterCommand = new RelayCommand(() => StepWindow(start: false, 1), () => !IsEncoding);
        CreateCloseCommands();

        SystemLamps = [GpuLamp, HandBrakeLamp, FfmpegLamp, LibraryLamp, ScratchLamp];
        _cqIndex = NearestDetent(_settings.Cq);
        for (var i = 0; i < CqDetents.Count; i++)
        {
            CqChips.Add(new CqChipViewModel(i, CqDetents[i], i == RecommendedCqIndex, index => CqIndex = index));
        }

        RefreshAll();
    }

    // ---- Commands ----

    public RelayCommand EncodeCommand { get; }

    public RelayCommand PauseCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand SkipCommand { get; }

    public RelayCommand ChooseLibraryCommand { get; }

    public RelayCommand ChooseScratchCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public RelayCommand CloseSettingsCommand { get; }

    public RelayCommand RescanCommand { get; }

    public RelayCommand WindowStartEarlierCommand { get; }

    public RelayCommand WindowStartLaterCommand { get; }

    public RelayCommand WindowEndEarlierCommand { get; }

    public RelayCommand WindowEndLaterCommand { get; }

    /// <summary>The run started by the last Encode press. Tests await it.</summary>
    public Task CurrentRun { get; private set; } = Task.CompletedTask;

    // ---- Header ----

    // The per-tool lamps no longer sit in the header: the run lamp names a problem when there is
    // one, and the service panel's System row lists every tool.
    public LampViewModel GpuLamp { get; } = new("NVENC");

    public LampViewModel HandBrakeLamp { get; } = new("HandBrakeCLI");

    public LampViewModel FfmpegLamp { get; } = new("ffmpeg");

    public LampViewModel LibraryLamp { get; } = new("Library");

    public LampViewModel ScratchLamp { get; } = new("Scratch");

    public ObservableCollection<LampViewModel> SystemLamps { get; }

    /// <summary>What the deck is doing, as a lamp and a word: Ready, Encoding, Paused or Needs attention.</summary>
    public LampViewModel RunLamp { get; } = new("Idle");

    /// <summary>What is wrong when the run lamp says so, one line per problem. Empty when nothing is.</summary>
    public string AttentionDetail => string.Join("\n", _attention.Select(a => a.Detail));

    // ---- Monitor A ----

    public ObservableCollection<ShowRowViewModel> Shows { get; } = [];

    /// <summary>
    /// The show under the cursor. Picking another one while the deck is idle loads it. During an
    /// encode the deck keeps its show, and the cursor goes back to it.
    /// </summary>
    public ShowRowViewModel? SelectedShow
    {
        get => _selectedShow;
        set
        {
            if (IsEncoding && value is not null && !value.IsLoaded)
            {
                _s.Dispatch(() => Raise());
                return;
            }

            if (Set(ref _selectedShow, value) && value is { IsLoaded: false } && !IsEncoding)
            {
                LoadShow(value.Scan);
            }
        }
    }

    /// <summary>The library's tooltip: its folder, and how many shows are hidden because there is nothing to do.</summary>
    public string LibraryTooltip
    {
        get
        {
            var shrunk = _hidden.Values.Count(h => h == HiddenReason.Shrunk);
            var hevc = _hidden.Values.Count(h => h == HiddenReason.Hevc);
            var other = _hidden.Values.Count(h => h == HiddenReason.Other);
            var parts = new List<string>();
            if (shrunk > 0)
            {
                parts.Add($"{shrunk} already shrunk");
            }

            if (hevc > 0)
            {
                parts.Add($"{hevc} already HEVC");
            }

            if (other > 0)
            {
                parts.Add($"{other} done");
            }

            var lines = new List<string>
            {
                string.IsNullOrWhiteSpace(_settings.LibraryRoot) ? "No library chosen" : _settings.LibraryRoot,
                $"{Shows.Count} to shrink",
            };
            if (parts.Count > 0)
            {
                lines.Add("Hidden: " + string.Join(", ", parts));
            }

            return string.Join("\n", lines);
        }
    }

    /// <summary>A line on the monitor for empty states and scans, shown only when there is something to say.</summary>
    public string MonitorMessage
    {
        get
        {
            if (_isRecovering)
            {
                return "RECOVERY CHECK...";
            }

            if (string.IsNullOrWhiteSpace(_settings.LibraryRoot))
            {
                return "NO LIBRARY. OPEN SETUP";
            }

            if (!_s.DirectoryExists(_settings.LibraryRoot))
            {
                return "LIBRARY NOT FOUND";
            }

            if (_isScanning)
            {
                return _scanStatus;
            }

            return Shows.Count == 0 ? "NOTHING LEFT TO SHRINK" : "";
        }
    }

    /// <summary>
    /// The latest failure in the deck's show, as monitor A prints it: the episode and why. It
    /// clears once that episode runs again. Empty when nothing failed.
    /// </summary>
    public string LastFailureText => LastFailed is { } t ? $"LAST FAIL {t.Code} · {t.Detail}".ToUpperInvariant() : "";

    /// <summary>The same failure in full, for the line's tooltip.</summary>
    public string LastFailureDetail => LastFailed is { } t ? $"{t.Code}: {t.Detail}" : "";

    /// <summary>Three lines about a show, for its row's tooltip.</summary>
    private string DetailFor(ShowRowViewModel row)
    {
        var s = row.Summary;
        var cq = _settings.CqForShow(row.FolderName);
        var eta = SizeEstimate.EncodeTime(s.QueueableFrames);
        var seasons = s.SeasonCount == 1 ? "1 season" : $"{s.SeasonCount} seasons";
        return $"{seasons}, {s.EpisodeCount} episodes\n"
            + $"{s.CodecLabel}, {s.QueueableCount} to do, {Format.Duration(eta).ToLowerInvariant()}\n"
            + $"Saves about {Format.WholeGigabytes(s.ProjectedSavings(cq))} of {Format.WholeGigabytes(s.TotalBytes)} GB at CQ {cq}";
    }

    // ---- Deck ----

    public bool IsTapeLoaded => _loaded is not null;

    public string LoadedName => _loaded?.Name ?? "";

    public string LabelTitle => _loaded?.Name.ToUpperInvariant() ?? "";

    public string LabelEpisode
    {
        get
        {
            var track = _current ?? NextQueued;
            if (track is null)
            {
                return _loaded is null ? "" : "All episodes handled";
            }

            return track.Title.Length > 0 ? $"{track.Code} · {track.Title}" : track.Code;
        }
    }

    /// <summary>The codec under the source reel, such as <c>H.264</c>.</summary>
    public string SourceCaption
    {
        get
        {
            var track = _current ?? NextQueued;
            return track is null ? "" : ShowSummary.CodecName(track.Record.VideoCodec);
        }
    }

    /// <summary>What the source reel's tooltip adds: how much of the file is left to read.</summary>
    public string SourceDetail
    {
        get
        {
            var track = _current ?? NextQueued;
            if (track is null)
            {
                return "";
            }

            var left = _current is null ? track.OriginalBytes : (long)(track.OriginalBytes * (1 - _progress));
            return $"Source, {Format.Gigabytes(left)} GB{(_current is null ? "" : " left")}";
        }
    }

    /// <summary>The codec under the output reel.</summary>
    public string OutputCaption => _loaded is null ? "" : "HEVC";

    public string OutputDetail => _current is null
        ? "Output, HEVC 10-bit"
        : $"Output, HEVC 10-bit, {Format.Gigabytes((long)(_current.OriginalBytes * Ratio * _progress))} GB so far";

    /// <summary>Source reel fill, 0 to 1: full before an encode, empty at the end.</summary>
    public double SourcePack => _current is not null ? 1 - _progress : NextQueued is not null ? 1 : 0;

    /// <summary>Output reel fill: it grows to the expected ratio, so it ends smaller.</summary>
    public double OutputPack => _current is not null ? _progress * Ratio : 0;

    public bool IsSpinning => _current is not null && _queue?.State == QueueState.Running;

    public string DeckAutomationName => _current is null
        ? (_loaded is null ? "Deck empty" : $"{_loaded.Name} loaded, idle")
        : $"{Phase} {_loaded?.Name} {_current.Code}, {Math.Floor((_transfer ?? _progress) * 100)} percent";

    // ---- Readouts ----

    public string EpisodeDigits => _current is { } c && c.Record.Episode is not null ? c.Code : "------";

    public string TapeTimeDigits => Format.TapeTime(_progress * (_current?.Record.DurationSeconds ?? 0)).PadLeft(5);

    public string TapeTotalText => _current?.Record.DurationSeconds is { } d ? "/ " + Format.TapeTime(d) : "/ --:--";

    /// <summary>What the current episode is going through, as the pipeline reports it.</summary>
    public string Phase => _current is null ? "" : PhaseWord(_phase);

    public string FpsDigits => IsSpinning && _fps is { } f && _phase == EpisodePhase.Encoding
        ? Math.Min(999, Math.Round(f)).ToString("0", CultureInfo.InvariantCulture).PadLeft(3)
        : "  0";

    public string PercentDigits => Math.Min(99.9, (_transfer ?? _progress) * 100).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(4);

    /// <summary>The episode counter's tooltip: which episode, and the speed and tape time the counter left off.</summary>
    public string EpisodeDetail => _current is null
        ? "Percent of the current episode"
        : $"{EpisodeDigits}, {FpsDigits.Trim()} frames per second, {TapeTimeDigits.Trim()} {TapeTotalText}";

    public string EpisodeCounterName => $"Episode {PercentDigits.Trim()} percent";

    /// <summary>Time left for the whole show, as <c>h:mm</c>. Dashes before a show is loaded.</summary>
    public string ShowLeftDigits
    {
        get
        {
            if (_loaded is null)
            {
                return "--:--";
            }

            var minutes = (int)Math.Min(99 * 60 + 59, Math.Ceiling(TimeLeft.TotalMinutes));
            return string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}:{minutes % 60:00}").PadLeft(5);
        }
    }

    public string ShowLeftDetail => _loaded is null
        ? "Time left for the whole show"
        : $"About {Format.Duration(TimeLeft).ToLowerInvariant()} left for {_tracks.Count(t => t.IsQueueable || t.Kind == TrackKind.Encoding)} episodes, at {Math.Round(SpeedFps)} frames per second";

    public string ShowLeftName => _loaded is null ? "Show time left, none" : $"Show time left, {Format.Duration(TimeLeft).ToLowerInvariant()}";

    // ---- Transport ----

    public bool IsEncoding => _queue is not null;

    public bool IsPaused => _queue?.State == QueueState.Paused;

    public LampState EncodeLamp => IsEncoding && !IsPaused ? LampState.Red : LampState.Off;

    public bool EncodeLampBlink => IsSpinning;

    // ---- Instruments ----

    public double BitrateMbps
    {
        get => _bitrate;
        private set => Set(ref _bitrate, value);
    }

    public double EncoderLoad
    {
        get => _encoderLoad;
        private set => Set(ref _encoderLoad, value);
    }

    /// <summary>The Quality row's choices in the service panel, one per detent.</summary>
    public ObservableCollection<CqChipViewModel> CqChips { get; } = [];

    /// <summary>The chosen detent. Picking one saves a CQ override for the show in the deck.</summary>
    public int CqIndex
    {
        get => _cqIndex;
        set
        {
            value = Math.Clamp(value, 0, CqDetents.Count - 1);
            if (!Set(ref _cqIndex, value))
            {
                return;
            }

            if (_loaded is not null && !IsEncoding)
            {
                var cq = CqDetents[value];
                if (cq == _settings.Cq)
                {
                    _settings.ShowCqOverrides.Remove(_loaded.Name);
                }
                else
                {
                    _settings.ShowCqOverrides[_loaded.Name] = cq;
                }

                Save();
            }

            RefreshDeck();
            RefreshCqChips();
            RefreshShowDetails();
        }
    }

    public int Cq => CqDetents[_cqIndex];

    /// <summary>True while a show is in the deck and nothing runs, so the Quality row can change.</summary>
    public bool CanEditQuality => _loaded is not null && !IsEncoding;

    /// <summary>
    /// The detent marked as recommended: the deck's default CQ, which shows without an override
    /// use. Choosing it clears the show's override. Out of the box that is
    /// <see cref="ShrinkRaySettings.DefaultCq"/>.
    /// </summary>
    public int RecommendedCqIndex => NearestDetent(_settings.Cq);

    /// <summary>True when the show in the deck has its own CQ rather than the default.</summary>
    public bool HasCqOverride => _loaded is not null && _settings.ShowCqOverrides.ContainsKey(_loaded.Name);

    public string EstimateText => $"Est −{Format.Percent(SizeEstimate.SavingsFor(Cq))} per ep";

    /// <summary>The line under the Quality chips: which show they set, and what that saves.</summary>
    public string CqCaption => _loaded is null
        ? "Pick a show to set its quality"
        : $"{_loaded.Name} · {EstimateText}";

    // ---- J-card ----

    public ObservableCollection<SeasonChipViewModel> Seasons { get; } = [];

    public ObservableCollection<TrackViewModel> Tracks { get; } = [];

    public int SelectedSeason => _selectedSeason;

    // ---- Encode window ----

    public string WindowText => string.Create(CultureInfo.InvariantCulture,
        $"{_settings.EncodeWindowStart:HH\\:mm} – {_settings.EncodeWindowEnd:HH\\:mm}");

    /// <summary>The window's opening time, as the service panel's readout shows it.</summary>
    public string WindowStartText => _settings.EncodeWindowStart.ToString("HH:mm", CultureInfo.InvariantCulture);

    public string WindowEndText => _settings.EncodeWindowEnd.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Moves a time to the next or previous <see cref="WindowStepMinutes"/> mark, wrapping at
    /// midnight. A time between marks, such as one typed into settings.json, lands on a mark.
    /// </summary>
    /// <param name="direction">1 for later, -1 for earlier.</param>
    public static TimeOnly StepTime(TimeOnly time, int direction)
    {
        const int day = 24 * 60;
        var minutes = (time.Hour * 60) + time.Minute;
        var mark = minutes / WindowStepMinutes * WindowStepMinutes;
        var next = direction > 0 ? mark + WindowStepMinutes
            : mark == minutes && time.Second == 0 ? mark - WindowStepMinutes
            : mark;
        next = ((next % day) + day) % day;
        return new TimeOnly(next / 60, next % 60);
    }

    // ---- Footer ----

    /// <summary>Bytes saved across every show ShrinkRay has shrunk, from the ledger.</summary>
    public long LifetimeSavedBytes => _s.Ledger.TotalSaved();

    /// <summary>Gigabytes to 999.9 GB, then terabytes, so five digits always hold it.</summary>
    public string LifetimeSavedUnit => Format.ToGigabytes(LifetimeSavedBytes) >= 999.95 ? "TB" : "GB";

    public string LifetimeSavedDigits
    {
        get
        {
            var gb = Format.ToGigabytes(LifetimeSavedBytes);
            var text = LifetimeSavedUnit == "TB"
                ? Math.Min(99.99, gb / 1000).ToString("0.00", CultureInfo.InvariantCulture)
                : gb.ToString("0.0", CultureInfo.InvariantCulture);
            return text.PadLeft(5);
        }
    }

    public string LifetimeSavedName => $"{LifetimeSavedDigits.Trim()} {(LifetimeSavedUnit == "TB" ? "terabytes" : "gigabytes")} saved all time";

    public double SavedGigabytes => Format.ToGigabytes(_tracks.Sum(t => t.SavedBytes));

    public string SavedDigits => Math.Min(999.9, SavedGigabytes).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5);

    /// <summary>The before and after bar's fill: the size after over the size before.</summary>
    public double AfterFraction
    {
        get
        {
            var before = _tracks.Sum(t => t.OriginalBytes);
            return before > 0 ? (double)_tracks.Sum(After) / before : 0;
        }
    }

    /// <summary>The bar's caption before the arrow: what the show weighs now, such as <c>22 GB</c>.</summary>
    public string SizeBeforeText => _loaded is null || _tracks.Count == 0 ? "" : Format.WholeGigabytes(_tracks.Sum(t => t.OriginalBytes)) + " GB";

    /// <summary>
    /// The caption after the arrow, such as <c>~7.8 GB · 3 OF 12 SHRUNK</c>. The tilde marks the
    /// size as an estimate while episodes remain.
    /// </summary>
    public string SizeAfterText
    {
        get
        {
            if (_loaded is null || _tracks.Count == 0)
            {
                return "";
            }

            var after = Format.WholeGigabytes(_tracks.Sum(After));
            var estimate = _tracks.Any(t => t.IsQueueable || t.Kind == TrackKind.Encoding) ? "~" : "";
            var shrunk = _tracks.Count(t => t.Kind == TrackKind.Shrunk);
            return $"{estimate}{after} GB · {shrunk} OF {_tracks.Count} SHRUNK";
        }
    }

    /// <summary>The whole caption, <c>22 GB → ~7.8 GB · 3 OF 12 SHRUNK</c>, for screen readers.</summary>
    public string SizeCaption => SizeBeforeText.Length == 0 ? "" : $"{SizeBeforeText} → {SizeAfterText}";

    // ---- Settings panel ----

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => Set(ref _isSettingsOpen, value);
    }

    /// <summary>A sentence for the user: why Encode did nothing, or what could not be saved.</summary>
    public string Notice
    {
        get => _notice;
        private set
        {
            if (Set(ref _notice, value))
            {
                Raise(nameof(MonitorNotice));
            }
        }
    }

    /// <summary>The notice as monitor A prints it.</summary>
    public string MonitorNotice => _notice.ToUpperInvariant();

    public bool ReplaceOriginals
    {
        get => _settings.ReplaceOriginals;
        set
        {
            if (_settings.ReplaceOriginals == value || IsEncoding)
            {
                Raise();
                return;
            }

            _settings.ReplaceOriginals = value;
            Save();
            Raise();
        }
    }

    public bool RestoreFonts
    {
        get => _settings.RestoreFonts;
        set
        {
            if (_settings.RestoreFonts == value || IsEncoding)
            {
                Raise();
                return;
            }

            _settings.RestoreFonts = value;
            Save();
            Raise();
        }
    }

    public bool EncodeWindowEnabled
    {
        get => _settings.EncodeWindowEnabled;
        set
        {
            if (_settings.EncodeWindowEnabled == value || IsEncoding)
            {
                Raise();
                return;
            }

            _settings.EncodeWindowEnabled = value;
            Save();
            RefreshWindow();
            Raise();
        }
    }

    public bool CanEditSettings => !IsEncoding;

    public string LibraryRoot => _settings.LibraryRoot;

    public string ScratchFolder => _settings.ScratchFolder;

    public Phosphor Phosphor => _prefs.Phosphor;

    public Chassis Chassis => _prefs.Chassis;

    public bool PhosphorAmber
    {
        get => _prefs.Phosphor == Phosphor.Amber;
        set => SetPhosphorIf(value, Phosphor.Amber);
    }

    public bool PhosphorGreen
    {
        get => _prefs.Phosphor == Phosphor.Green;
        set => SetPhosphorIf(value, Phosphor.Green);
    }

    public bool PhosphorIce
    {
        get => _prefs.Phosphor == Phosphor.Ice;
        set => SetPhosphorIf(value, Phosphor.Ice);
    }

    public bool ChassisBeige
    {
        get => _prefs.Chassis == Chassis.Beige;
        set => SetChassisIf(value, Chassis.Beige);
    }

    public bool ChassisGraphite
    {
        get => _prefs.Chassis == Chassis.Graphite;
        set => SetChassisIf(value, Chassis.Graphite);
    }

    private double Ratio => SizeEstimate.RatioFor(IsEncoding ? _runCq : Cq);

    private TrackViewModel? NextQueued => _tracks.FirstOrDefault(t => t.IsQueueable);

    private TrackViewModel? LastFailed => _tracks
        .Where(t => t.Kind == TrackKind.Failed)
        .OrderByDescending(t => t.FailedAt ?? DateTimeOffset.MinValue)
        .FirstOrDefault();

    /// <summary>The run lamp's word for a phase.</summary>
    public static string PhaseWord(EpisodePhase phase) => phase switch
    {
        EpisodePhase.Probing => "Reading",
        EpisodePhase.Copying => "Copying",
        EpisodePhase.Encoding => "Encoding",
        EpisodePhase.RestoringFonts => "Restoring fonts",
        EpisodePhase.Verifying => "Checking",
        EpisodePhase.Uploading => "Uploading",
        _ => "Swapping",
    };

    /// <summary>The J-card status for a phase other than the encode, which shows its percent.</summary>
    public static string PhaseStatus(EpisodePhase phase) => phase switch
    {
        EpisodePhase.Probing => "READ",
        EpisodePhase.Copying => "COPY",
        EpisodePhase.RestoringFonts => "FONTS",
        EpisodePhase.Verifying => "CHECK",
        EpisodePhase.Uploading => "UPLOAD",
        _ => "SWAP",
    };

    /// <summary>The frame rate the time left is figured at: the live encode rate, else the measured one.</summary>
    private double SpeedFps => IsSpinning && _fps is > 1 ? _fps.Value : SizeEstimate.MeasuredFps;

    /// <summary>Encode time left for every episode still to do, less what the current one has done.</summary>
    private TimeSpan TimeLeft
    {
        get
        {
            var frames = _tracks.Where(t => t.IsQueueable || t.Kind == TrackKind.Encoding).Sum(t => t.Record.FrameCount ?? 0);
            if (frames <= 0)
            {
                return TimeSpan.Zero;
            }

            var done = _current?.Record.FrameCount is { } f ? (long)(f * _progress) : 0;
            return SizeEstimate.EncodeTime(Math.Max(0, frames - done), SpeedFps);
        }
    }

    private long After(TrackViewModel t) => t.Kind switch
    {
        TrackKind.Shrunk => t.NewBytes ?? t.OriginalBytes,
        TrackKind.Queued or TrackKind.Failed or TrackKind.Encoding => (long)(t.OriginalBytes * Ratio),
        _ => t.OriginalBytes,
    };

    private static int NearestDetent(int cq) =>
        CqDetents.Select((d, i) => (Distance: Math.Abs(d - cq), Index: i)).OrderBy(x => x.Distance).ThenByDescending(x => x.Index).First().Index;
}
