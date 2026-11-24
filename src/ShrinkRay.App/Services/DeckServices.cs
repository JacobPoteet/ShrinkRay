using ShrinkRay.Core.Library;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Processes;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Swap;
using ShrinkRay.Theme;

namespace ShrinkRay.App.Services;

/// <summary>
/// Everything the deck view model talks to. <see cref="Composition"/> builds the real set;
/// tests build one from fakes.
/// </summary>
public sealed class DeckServices
{
    public required ILibraryDisk Disk { get; init; }

    public required IMediaProbe Probe { get; init; }

    public required ProbeCache Cache { get; init; }

    public required SwapJournal Journal { get; init; }

    public required ShrinkLedger Ledger { get; init; }

    /// <summary>Builds the episode runner for one run, from a snapshot of the settings.</summary>
    public required Func<ShrinkRaySettings, IEpisodeRunner> CreateRunner { get; init; }

    /// <summary>Settles anything the journal left mid-swap. Runs before the first scan.</summary>
    public required Func<ShrinkRaySettings, CancellationToken, Task<IReadOnlyList<RecoveryAction>>> Recover { get; init; }

    public IProcessGate? Gate { get; init; }

    public IGpuMonitor? Gpu { get; init; }

    public IFolderPicker? FolderPicker { get; init; }

    public Action<ShrinkRaySettings> SaveSettings { get; init; } = _ => { };

    public Action<DeckPreferences> SavePreferences { get; init; } = _ => { };

    public Action<ProbeCache> SaveCache { get; init; } = _ => { };

    /// <summary>Applies a palette. The app passes <see cref="ThemeManager.Apply"/>.</summary>
    public Action<Phosphor, Chassis> ApplyTheme { get; init; } = (_, _) => { };

    /// <summary>Finds a tool from its configured name; null when missing.</summary>
    public Func<string, string?> ResolveTool { get; init; } = name => name;

    /// <summary>Size of a file in bytes, or null when it does not exist.</summary>
    public Func<string, long?> FileSize { get; init; } = path =>
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    };

    public Func<string, bool> DirectoryExists { get; init; } = Directory.Exists;

    public Func<DateTime> Now { get; init; } = () => DateTime.Now;

    /// <summary>Runs an action on the UI thread. Tests run it inline.</summary>
    public Action<Action> Dispatch { get; init; } = action => action();

    /// <summary>The wait between encode window checks.</summary>
    public Func<TimeSpan, CancellationToken, Task>? Delay { get; init; }
}
