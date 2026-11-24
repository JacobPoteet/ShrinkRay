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
/// The main deck: monitor A lists candidate shows, Load tape puts one in the deck, and Encode
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
    private double? _fps;
    private EpisodePhase _phase;

    private double _bitrate;
    private double _encoderLoad;
    private DateTime _lastLoadPoll = DateTime.MinValue;
    private bool _loadPollBusy;
    private int _lastMinute = -1;

    private bool _isRecovering;
    private bool _isScanning;
    private string _scanStatus = "";
    private string _recoveryText = "";
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

        LoadTapeCommand = new RelayCommand(LoadTape, () => _selectedShow is not null && !IsEncoding);
        EncodeCommand = new RelayCommand(Encode, () => (_loaded is not null && !_isRecovering) || IsEncoding);
        PauseCommand = new RelayCommand(TogglePause);
        StopCommand = new RelayCommand(Stop);
        SkipCommand = new RelayCommand(Skip);
        EjectCommand = new RelayCommand(Eject, () => _loaded is not null && !IsEncoding);
        ResetCqCommand = new RelayCommand(ResetCq, () => HasCqOverride && !IsEncoding);
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
        RuleLamps = [new LampViewModel("Skip HEVC files", LampState.Ok), KeepLamp, FontsLamp, new LampViewModel("Same container", LampState.Ok)];
        _cqIndex = NearestDetent(_settings.Cq);
        RefreshAll();
    }

    // ---- Commands ----

    public RelayCommand LoadTapeCommand { get; }

    public RelayCommand EncodeCommand { get; }

    public RelayCommand PauseCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand SkipCommand { get; }

    public RelayCommand EjectCommand { get; }

    /// <summary>Clears the loaded show's CQ override, turning the knob back to the default.</summary>
    public RelayCommand ResetCqCommand { get; }

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

    public LampViewModel GpuLamp { get; } = new("NVENC");

    public LampViewModel HandBrakeLamp { get; } = new("HandBrakeCLI");

    public LampViewModel FfmpegLamp { get; } = new("ffmpeg");

    public LampViewModel LibraryLamp { get; } = new("Library");

    public LampViewModel ScratchLamp { get; } = new("Scratch");

    public ObservableCollection<LampViewModel> SystemLamps { get; }

    /// <summary>What the deck is doing, as a lamp and a word.</summary>
    public LampViewModel RunLamp { get; } = new("Idle");

    // ---- Monitor A ----

    public ObservableCollection<ShowRowViewModel> Shows { get; } = [];

    public ShowRowViewModel? SelectedShow
    {
        get => _selectedShow;
        set
        {
            if (Set(ref _selectedShow, value))
            {
                Raise(nameof(CursorInfo), nameof(LoadLampState), nameof(LoadLampBlink));
                LoadTapeCommand.Refresh();
            }
        }
    }

    /// <summary>
    /// The last two folders of the library root, such as <c>PLEX/TV</c>. Drive letters and a
    /// share's host name are left off.
    /// </summary>
    public string LibraryLabel
    {
        get
        {
            var root = _settings.LibraryRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                return "NO LIBRARY";
            }

            var parts = root.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (root.StartsWith(@"\\", StringComparison.Ordinal) && parts.Count > 0)
            {
                parts.RemoveAt(0);
            }

            parts.RemoveAll(p => p.EndsWith(':'));
            return parts.Count == 0 ? root.ToUpperInvariant() : string.Join("/", parts.TakeLast(2)).ToUpperInvariant();
        }
    }

    public string ShowCountText => _isScanning && Shows.Count == 0 ? "" : $"{Shows.Count} TO SHRINK";

    public string HiddenText
    {
        get
        {
            var shrunk = _hidden.Values.Count(h => h == HiddenReason.Shrunk);
            var hevc = _hidden.Values.Count(h => h == HiddenReason.Hevc);
            var other = _hidden.Values.Count(h => h == HiddenReason.Other);
            var parts = new List<string>();
            if (shrunk > 0)
            {
                parts.Add($"{shrunk} SHRUNK");
            }

            if (hevc > 0)
            {
                parts.Add($"{hevc} HEVC");
            }

            if (other > 0)
            {
                parts.Add($"{other} DONE");
            }

            return parts.Count == 0 ? "" : "HIDDEN: " + string.Join(" · ", parts);
        }
    }

    /// <summary>A line on the monitor for empty states, scans and recovery.</summary>
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
                return "NO LIBRARY. PRESS CHOOSE";
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

    public string RecoveryText
    {
        get => _recoveryText;
        private set => Set(ref _recoveryText, value);
    }

    /// <summary>Three lines about the show under the cursor. Sizes are in GB, as the column header says.</summary>
    public string CursorInfo
    {
        get
        {
            if (_selectedShow is not { } row)
            {
                return "";
            }

            var s = row.Summary;
            var cq = _settings.CqForShow(row.FolderName);
            var eta = SizeEstimate.EncodeTime(s.QueueableFrames);
            var seasons = s.SeasonCount == 1 ? "1 SEASON" : $"{s.SeasonCount} SEASONS";
            return $"{seasons} · {s.EpisodeCount} EPS\n"
                + $"{s.CodecLabel} · {s.QueueableCount} TO DO · {Format.Duration(eta)}\n"
                + $"SAVE ~{Format.WholeGigabytes(s.ProjectedSavings(cq))} OF {Format.WholeGigabytes(s.TotalBytes)} @ CQ{cq}";
        }
    }

    /// <summary>Load tape's lamp blinks when the cursor sits on a show that is not in the deck.</summary>
    public LampState LoadLampState => _selectedShow is not null && !string.Equals(_selectedShow.FolderName, _loaded?.Name, StringComparison.OrdinalIgnoreCase)
        ? LampState.Active
        : LampState.Off;

    public bool LoadLampBlink => LoadLampState == LampState.Active;

    public ObservableCollection<LampViewModel> RuleLamps { get; }

    public LampViewModel KeepLamp { get; } = new("Keep under 15%", LampState.Ok);

    public LampViewModel FontsLamp { get; } = new("Restore fonts", LampState.Ok);

    // ---- Deck ----

    public bool IsTapeLoaded => _loaded is not null;

    public string LoadedName => _loaded?.Name ?? "";

    public string LabelTitle => _loaded?.Name.ToUpperInvariant() ?? "";

    public string LabelSide
    {
        get
        {
            if (_loaded is null)
            {
                return "";
            }

            var seasons = _tracks.Select(t => t.Record.Season).Where(s => s > 0).Distinct().Count();
            var total = _tracks.Sum(t => t.OriginalBytes);
            return $"Side A · {seasons} {(seasons == 1 ? "season" : "seasons")} · {_tracks.Count} episodes · {Format.WholeGigabytes(total)} GB";
        }
    }

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

    public string SourceText
    {
        get
        {
            var track = _current ?? NextQueued;
            if (track is null)
            {
                return "Source";
            }

            var left = _current is null ? track.OriginalBytes : (long)(track.OriginalBytes * (1 - _progress));
            return $"Source · {ShowSummary.CodecName(track.Record.VideoCodec)} · {Format.Gigabytes(left)} GB {(_current is null ? "" : "left")}".TrimEnd();
        }
    }

    public string OutputText => _current is null
        ? "Output · HEVC 10-bit"
        : $"Output · HEVC 10-bit · {Format.Gigabytes((long)(_current.OriginalBytes * Ratio * _progress))} GB";

    /// <summary>Source reel fill, 0 to 1: full before an encode, empty at the end.</summary>
    public double SourcePack => _current is not null ? 1 - _progress : NextQueued is not null ? 1 : 0;

    /// <summary>Output reel fill: it grows to the expected ratio, so it ends smaller.</summary>
    public double OutputPack => _current is not null ? _progress * Ratio : 0;

    public bool IsSpinning => _current is not null && _queue?.State == QueueState.Running;

    public string DeckAutomationName => _current is null
        ? (_loaded is null ? "Deck empty" : $"{_loaded.Name} loaded, idle")
        : $"{Phase} {_loaded?.Name} {_current.Code}, {Math.Floor(_progress * 100)} percent";

    // ---- Readouts ----

    public string EpisodeDigits => _current is { } c && c.Record.Episode is not null ? c.Code : "------";

    public string TapeTimeDigits => Format.TapeTime(_progress * (_current?.Record.DurationSeconds ?? 0)).PadLeft(5);

    public string TapeTotalText => _current?.Record.DurationSeconds is { } d ? "/ " + Format.TapeTime(d) : "/ --:--";

    /// <summary>What the current episode is going through, as the pipeline reports it.</summary>
    public string Phase => _current is null ? "" : PhaseWord(_phase);

    public string FpsDigits => IsSpinning && _fps is { } f && _phase == EpisodePhase.Encoding
        ? Math.Min(999, Math.Round(f)).ToString("0", CultureInfo.InvariantCulture).PadLeft(3)
        : "  0";

    public string PercentDigits => Math.Min(99.9, _progress * 100).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(4);

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

    public IList<string> CqLabels { get; } = CqDetents.Select(c => c.ToString(CultureInfo.InvariantCulture)).ToList();

    /// <summary>The knob. Turning it saves a CQ override for the show in the deck.</summary>
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
            ResetCqCommand.Refresh();
            Raise(nameof(CursorInfo));
        }
    }

    public int Cq => CqDetents[_cqIndex];

    public bool IsKnobEnabled => _loaded is not null && !IsEncoding;

    /// <summary>
    /// The detent the knob marks as recommended: the deck's default CQ, which shows without an
    /// override use and Reset returns to. Out of the box that is <see cref="ShrinkRaySettings.DefaultCq"/>.
    /// </summary>
    public int RecommendedCqIndex => NearestDetent(_settings.Cq);

    /// <summary>True when the show in the deck has its own CQ rather than the default.</summary>
    public bool HasCqOverride => _loaded is not null && _settings.ShowCqOverrides.ContainsKey(_loaded.Name);

    /// <summary>The readout under the knob: the CQ the show encodes at, and whether it is the show's own or the default.</summary>
    public string CqSourceText => string.Create(CultureInfo.InvariantCulture,
        $"CQ {_settings.CqForShow(_loaded?.Name)} · {(HasCqOverride ? "Show" : "Default")}");

    public string EstimateText => $"Est −{Format.Percent(SizeEstimate.SavingsFor(Cq))} per ep";

    // ---- J-card ----

    public ObservableCollection<SeasonChipViewModel> Seasons { get; } = [];

    public ObservableCollection<TrackViewModel> Tracks { get; } = [];

    public int SelectedSeason => _selectedSeason;

    public string JCardHeader
    {
        get
        {
            if (_loaded is null)
            {
                return "";
            }

            var count = _tracks.Count(t => t.Record.Season == _selectedSeason);
            return $"{SeasonName(_selectedSeason)} · {count} {(count == 1 ? "episode" : "episodes")}";
        }
    }

    public string SeasonFooter
    {
        get
        {
            if (_loaded is null)
            {
                return "Load a tape to see its track list";
            }

            var season = _tracks.Where(t => t.Record.Season == _selectedSeason).ToList();
            return $"{SeasonName(_selectedSeason)} · {Format.Gigabytes(season.Sum(t => t.OriginalBytes))} GB now · ~{Format.Gigabytes(season.Sum(After))} GB after";
        }
    }

    // ---- Encode window ----

    public bool WindowEnabled => _settings.EncodeWindowEnabled;

    public double WindowStartHours => _settings.EncodeWindowStart.ToTimeSpan().TotalHours;

    public double WindowEndHours => _settings.EncodeWindowEnd.ToTimeSpan().TotalHours;

    public double NowHours => _s.Now().TimeOfDay.TotalHours;

    public string WindowText => string.Create(CultureInfo.InvariantCulture,
        $"{_settings.EncodeWindowStart:HH\\:mm} – {_settings.EncodeWindowEnd:HH\\:mm}");

    public LampViewModel WindowLamp { get; } = new("Off");

    /// <summary>The window's opening time, as the service panel's readout shows it.</summary>
    public string WindowStartText => _settings.EncodeWindowStart.ToString("HH:mm", CultureInfo.InvariantCulture);

    public string WindowEndText => _settings.EncodeWindowEnd.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A start equal to the end opens the window all day, as the settings read it.</summary>
    public string WindowDymo => !_settings.EncodeWindowEnabled || _settings.EncodeWindowStart == _settings.EncodeWindowEnd ? "Runs any time"
        : _settings.EncodeWindowStart > _settings.EncodeWindowEnd ? "Runs overnight"
        : "Runs by day";

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

    public string FooterTitle => _loaded is null ? "Space reclaimed" : "Space reclaimed · " + _loaded.Name;

    public double SavedGigabytes => Format.ToGigabytes(_tracks.Sum(t => t.SavedBytes));

    public string SavedDigits => Math.Min(999.9, SavedGigabytes).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5);

    public string BeforeText => Format.WholeGigabytes(_tracks.Sum(t => t.OriginalBytes));

    public string AfterText => Format.WholeGigabytes(_tracks.Sum(After));

    public double AfterFraction
    {
        get
        {
            var before = _tracks.Sum(t => t.OriginalBytes);
            return before > 0 ? (double)_tracks.Sum(After) / before : 0;
        }
    }

    public string EpisodesDoneText
    {
        get
        {
            if (_loaded is null)
            {
                return "";
            }

            var shrunk = _tracks.Count(t => t.Kind == TrackKind.Shrunk);
            var text = $"{shrunk} of {_tracks.Count} episodes shrunk";
            var frames = _tracks.Where(t => t.IsQueueable || t.Kind == TrackKind.Encoding).Sum(t => t.Record.FrameCount ?? 0);
            if (frames > 0)
            {
                var done = _current?.Record.FrameCount is { } f ? (long)(f * _progress) : 0;
                var fps = IsSpinning && _fps is > 1 ? _fps.Value : SizeEstimate.MeasuredFps;
                text += " · " + Format.Duration(SizeEstimate.EncodeTime(frames - done, fps)) + " left";
            }

            return text;
        }
    }

    /// <summary>Episodes handled (shrunk, kept or skipped) over all episodes, for the LED bar.</summary>
    public double HandledFraction => _tracks.Count == 0 ? 0
        : (double)_tracks.Count(t => t.Kind is TrackKind.Shrunk or TrackKind.Kept or TrackKind.Skipped) / _tracks.Count;

    public LampViewModel OriginalsLamp { get; } = new("Replaced after checks pass", LampState.Ok);

    public LampViewModel RecycleLamp { get; } = new("Old file to NAS recycle bin", LampState.Ok);

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
            RefreshFooterLamps();
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
            RefreshRuleLamps();
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
            RefreshWindow(force: true);
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

    private static string SeasonName(int season) => season == 0 ? "Specials" : "Season " + season;

    private long After(TrackViewModel t) => t.Kind switch
    {
        TrackKind.Shrunk => t.NewBytes ?? t.OriginalBytes,
        TrackKind.Queued or TrackKind.Failed or TrackKind.Encoding => (long)(t.OriginalBytes * Ratio),
        _ => t.OriginalBytes,
    };

    private static int NearestDetent(int cq) =>
        CqDetents.Select((d, i) => (Distance: Math.Abs(d - cq), Index: i)).OrderBy(x => x.Distance).ThenByDescending(x => x.Index).First().Index;
}
