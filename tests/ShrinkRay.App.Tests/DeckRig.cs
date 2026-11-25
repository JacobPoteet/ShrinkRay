using System.Collections.Concurrent;
using ShrinkRay.App.Services;
using ShrinkRay.App.ViewModels;
using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Library;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Processes;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Swap;
using ShrinkRay.Theme;

namespace ShrinkRay.App.Tests;

/// <summary>A deck wired to fakes: an in-memory library under <c>X:\TV</c>, a scripted runner and a recording gate.</summary>
internal sealed class DeckRig
{
    public const string Root = @"X:\TV";
    public const long Gigabyte = 1_000_000_000;

    public FakeDisk Disk { get; } = new();
    public FakeProbe Probe { get; } = new();
    public MemoryJournal Store { get; } = new();
    public SwapJournal Journal { get; private set; }
    public ShrinkLedger Ledger { get; } = new();
    public ScriptedRunner Runner { get; }
    public RecordingGate Gate { get; } = new();
    public List<string> Log { get; } = [];
    public List<(Phosphor, Chassis)> Themes { get; } = [];
    public ConcurrentDictionary<string, long> Sizes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ShrinkRaySettings Settings { get; } = new() { LibraryRoot = Root, ScratchFolder = @"C:\Scratch" };
    public List<ShrinkRaySettings> RunSettings { get; } = [];
    public int SettingsSaves { get; private set; }
    public DeckPreferences? SavedPreferences { get; private set; }
    public IReadOnlyList<RecoveryAction> RecoveryActions { get; set; } = [];
    public string? PickedFolder { get; set; }

    /// <summary>Tool names that resolve to nothing, as when an exe is not installed.</summary>
    public HashSet<string> MissingTools { get; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime Now { get; set; } = new(2026, 11, 24, 2, 10, 0);

    /// <summary>Held while a dispatched update runs, so the fake dispatcher handles one thing at a time.</summary>
    private readonly object _ui = new();

    public DeckRig()
    {
        Journal = new SwapJournal(Store);
        Runner = new ScriptedRunner(this);
    }

    /// <summary>
    /// Runs <paramref name="act"/> between the deck's dispatched updates, as a click on the UI
    /// thread does. Use it to act on state the queue's thread is still updating.
    /// </summary>
    public void OnUiThread(Action act)
    {
        lock (_ui)
        {
            act();
        }
    }

    /// <summary>Reads the journal store again, as a restart does. Entries appended to <see cref="Store"/> take effect.</summary>
    public void ReopenJournal() => Journal = new SwapJournal(Store);

    public static string Episode(string show, int season, int episode, string ext = ".mkv") =>
        $@"{Root}\{show}\Season {season:00}\{show} - S{season:00}E{episode:00} - Title {episode}{ext}";

    public string Add(string show, int season, int episode, string codec = "h264", long size = Gigabyte)
    {
        var path = Episode(show, season, episode);
        Disk.AddFile(path, size);
        Probe.Results[path] = new MediaInfo
        {
            FileName = path,
            FormatName = "matroska,webm",
            DurationSeconds = 1320,
            Streams =
            [
                new MediaStream { Index = 0, Type = StreamType.Video, CodecName = codec, FrameRate = 25 },
                new MediaStream { Index = 1, Type = StreamType.Audio, CodecName = "aac" },
            ],
        };
        return path;
    }

    public MainViewModel Create(DeckPreferences? preferences = null)
    {
        var services = new DeckServices
        {
            Disk = Disk,
            Probe = Probe,
            Cache = new ProbeCache(),
            Journal = Journal,
            Ledger = Ledger,
            Gate = Gate,
            CreateRunner = settings =>
            {
                RunSettings.Add(settings);
                Log.Add("runner");
                return Runner;
            },
            Recover = (_, _) =>
            {
                Log.Add("recover");
                return Task.FromResult(RecoveryActions);
            },
            SaveSettings = _ => SettingsSaves++,
            SavePreferences = p => SavedPreferences = p,
            ApplyTheme = (p, c) => Themes.Add((p, c)),
            ResolveTool = name => MissingTools.Contains(name) ? null : name,
            FileSize = path => Sizes.TryGetValue(path, out var size) ? size : Disk.Stamp(path)?.Size,
            DirectoryExists = path => Disk.DirectoryExists(path) || path.Length <= 3,
            Now = () => Now,
            FolderPicker = new FakePicker(this),
            Dispatch = action => OnUiThread(action),
            Delay = (_, ct) => Task.Delay(1, ct),
        };
        return new MainViewModel(services, Settings, preferences ?? new DeckPreferences(), @"C:\Default\Scratch");
    }

    /// <summary>Starts the deck and loads <paramref name="show"/>.</summary>
    public async Task<MainViewModel> StartedWith(string show)
    {
        var vm = Create(new DeckPreferences { LastShow = show });
        await vm.StartAsync();
        Assert.Equal(show, vm.LoadedName);
        return vm;
    }

    internal sealed class FakePicker(DeckRig rig) : IFolderPicker
    {
        public string? PickFolder(string title, string? initialFolder) => rig.PickedFolder;
    }
}

internal sealed class FakeDisk : ILibraryDisk
{
    private readonly Dictionary<string, FileStamp> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Listed { get; } = [];

    public void AddFile(string path, long size)
    {
        _files[path] = new FileStamp(size, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        for (var dir = Path.GetDirectoryName(path); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            _folders.Add(dir);
        }
    }

    public bool DirectoryExists(string path) => _folders.Contains(path.TrimEnd('\\'));

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        lock (Listed)
        {
            Listed.Add(path);
        }

        return _folders.Where(f => string.Equals(Path.GetDirectoryName(f), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public IEnumerable<string> EnumerateFiles(string path) =>
        _files.Keys.Where(f => string.Equals(Path.GetDirectoryName(f), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();

    public FileStamp? Stamp(string path) => _files.TryGetValue(path, out var stamp) ? stamp : null;
}

internal sealed class FakeProbe : IMediaProbe
{
    public ConcurrentDictionary<string, MediaInfo> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken) => Task.FromResult(Results[path]);

    public Task<MediaInfo> DetectClosedCaptionsAsync(string path, MediaInfo info, CancellationToken cancellationToken) =>
        Task.FromResult(info);
}

internal sealed class MemoryJournal : IJournalStore
{
    private readonly List<JournalEntry> _entries = [];

    public void Append(JournalEntry entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }
    }

    public IReadOnlyList<JournalEntry> ReadAll()
    {
        lock (_entries)
        {
            return _entries.ToList();
        }
    }
}

internal sealed class RecordingGate : IProcessGate
{
    public bool IsPaused { get; private set; }

    public void Pause() => IsPaused = true;

    public void Resume() => IsPaused = false;
}

/// <summary>
/// Runs episodes from a script. A replaced episode shrinks to 36% on the fake disk. A path in
/// <see cref="Hold"/> reports 50% and waits until released or cancelled.
/// </summary>
internal sealed class ScriptedRunner(DeckRig rig) : IEpisodeRunner
{
    private readonly SemaphoreSlim _held = new(0);

    public ConcurrentDictionary<string, PipelineResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentDictionary<string, TaskCompletionSource> Hold { get; } = new(StringComparer.OrdinalIgnoreCase);

    private IProgress<EpisodeProgress>? _progress;

    public ConcurrentQueue<string> Ran { get; } = new();

    /// <summary>The token each episode ran with, to tell a cancelled episode from one left to finish.</summary>
    public ConcurrentDictionary<string, CancellationToken> Tokens { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Report(double percent) =>
        _progress?.Report(new EpisodeProgress(EpisodePhase.Encoding, new EncodeProgress(1, 1, percent, 170, 165, null)));

    public void Report(double percent, double fps) =>
        _progress?.Report(new EpisodeProgress(EpisodePhase.Encoding, new EncodeProgress(1, 1, percent, fps, fps, null)));

    public void Report(EpisodePhase phase) => _progress?.Report(new EpisodeProgress(phase));

    public void Report(EpisodePhase phase, double fraction) => _progress?.Report(new EpisodeProgress(phase, Fraction: fraction));

    public async Task<PipelineResult> RunAsync(string sourcePath, IProgress<EpisodeProgress>? progress, CancellationToken cancellationToken)
    {
        Ran.Enqueue(sourcePath);
        Tokens[sourcePath] = cancellationToken;
        _progress = progress;
        progress?.Report(new EpisodeProgress(EpisodePhase.Probing));
        progress?.Report(new EpisodeProgress(EpisodePhase.Copying));
        progress?.Report(new EpisodeProgress(EpisodePhase.Encoding, new EncodeProgress(1, 1, 50, 170, 165, null)));
        if (Hold.TryGetValue(sourcePath, out var hold))
        {
            _held.Release();
            await hold.Task.WaitAsync(cancellationToken);
        }

        var result = Results.GetValueOrDefault(sourcePath) ?? new PipelineResult(PipelineOutcome.Replaced, "Replaced");
        if (result.Outcome == PipelineOutcome.Replaced)
        {
            rig.Sizes[sourcePath] = (long)(rig.Disk.Stamp(sourcePath)!.Value.Size * 0.36);
        }

        return result;
    }

    public async Task WaitUntilHeldAsync() =>
        Assert.True(await _held.WaitAsync(TimeSpan.FromSeconds(5)), "No episode reached its hold in time.");
}
