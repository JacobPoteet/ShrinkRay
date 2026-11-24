using ShrinkRay.Core.Library;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Processes;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Swap;
using ShrinkRay.Theme;

namespace ShrinkRay.App.Services;

/// <summary>Wires the real engine: ffprobe, HandBrakeCLI, ffmpeg, the journal and the files on disk.</summary>
public static class Composition
{
    /// <param name="settings">The loaded settings. Scans use its ffprobe path.</param>
    public static DeckServices Create(AppPaths paths, ShrinkRaySettings settings, Action<Action> dispatch)
    {
        var gate = new ProcessGate();
        var runner = new ProcessRunner(gate);
        var plainRunner = new ProcessRunner();
        var fs = new PhysicalFileSystem();
        var journal = new SwapJournal(new JsonLinesJournalStore(paths.Journal));

        return new DeckServices
        {
            Disk = new PhysicalLibraryDisk(),
            Probe = new FfprobeMediaProbe(plainRunner, ToolLocator.ResolveOnThisMachine(settings.FfprobePath) ?? settings.FfprobePath),
            Cache = ProbeCache.Load(paths.ProbeCache),
            Journal = journal,
            Ledger = new ShrinkLedger(paths.Ledger),
            Gate = gate,
            Gpu = new NvidiaSmiMonitor(plainRunner),
            FolderPicker = new WpfFolderPicker(),
            CreateRunner = settings => CreateRunner(settings, runner, fs, journal),
            Recover = (settings, ct) => RecoverAsync(settings, plainRunner, fs, journal, ct),
            SaveSettings = settings => SettingsStore.Save(paths.Settings, settings),
            SavePreferences = prefs => prefs.Save(paths.Preferences),
            SaveCache = cache => cache.Save(paths.ProbeCache),
            ApplyTheme = ThemeManager.Apply,
            ResolveTool = ToolLocator.ResolveOnThisMachine,
            Dispatch = dispatch,
        };
    }

    /// <summary>Settings with each tool name swapped for the path ToolLocator found.</summary>
    public static ShrinkRaySettings WithResolvedTools(ShrinkRaySettings settings)
    {
        var s = SettingsStore.Normalize(settings);
        s.HandBrakeCliPath = ToolLocator.ResolveOnThisMachine(s.HandBrakeCliPath) ?? s.HandBrakeCliPath;
        s.FfprobePath = ToolLocator.ResolveOnThisMachine(s.FfprobePath) ?? s.FfprobePath;
        s.FfmpegPath = ToolLocator.ResolveOnThisMachine(s.FfmpegPath) ?? s.FfmpegPath;
        return s;
    }

    private static IEpisodeRunner CreateRunner(ShrinkRaySettings settings, IProcessRunner runner, IFileSystem fs, SwapJournal journal)
    {
        var s = WithResolvedTools(settings);
        var probe = new FfprobeMediaProbe(runner, s.FfprobePath);
        var decoder = new FfmpegDecodeChecker(runner, s.FfmpegPath);
        var recovery = new SwapRecovery(fs, journal, new ProbeFinalFileCheck(fs, probe, decoder, s));
        return new EpisodePipeline(
            fs,
            probe,
            new HandBrakeEncoder(runner, s.HandBrakeCliPath),
            new FfmpegFontRestorer(runner, s.FfmpegPath),
            decoder,
            journal,
            new SwapExecutor(fs, journal, recovery),
            s);
    }

    private static Task<IReadOnlyList<RecoveryAction>> RecoverAsync(
        ShrinkRaySettings settings, IProcessRunner runner, IFileSystem fs, SwapJournal journal, CancellationToken ct)
    {
        var s = WithResolvedTools(settings);
        var probe = new FfprobeMediaProbe(runner, s.FfprobePath);
        var check = new ProbeFinalFileCheck(fs, probe, new FfmpegDecodeChecker(runner, s.FfmpegPath), s);
        var folders = string.IsNullOrWhiteSpace(s.LibraryRoot) || !Directory.Exists(s.LibraryRoot) ? [] : new[] { s.LibraryRoot };
        return Task.Run(
            async () =>
            {
                var actions = await new SwapRecovery(fs, journal, check).RecoverAsync(folders, ct);
                EpisodePipeline.RemoveCancelledWork(fs, journal, s.ScratchFolder);
                return actions;
            },
            ct);
    }
}
