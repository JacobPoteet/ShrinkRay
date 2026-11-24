using ShrinkRay.Core.Media;

namespace ShrinkRay.Core.Library;

/// <summary>
/// Finds show folders under a library root and probes each episode, reusing cached probes for
/// files whose size and last write time have not changed.
/// </summary>
public sealed class LibraryScanner
{
    /// <summary>Files with these extensions are probed. Unsupported ones show up as skipped.</summary>
    public static readonly IReadOnlySet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".ts", ".m2ts", ".webm", ".mpg", ".mpeg",
    };

    // Plex extras folders hold trailers and featurettes, not episodes.
    private static readonly HashSet<string> ExtrasFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Behind The Scenes", "Deleted Scenes", "Featurettes", "Interviews", "Scenes", "Shorts", "Trailers", "Other", "Extras",
        "System Volume Information",
    };

    private const int MaxDepth = 3;

    private readonly ILibraryDisk _disk;
    private readonly IMediaProbe _probe;
    private readonly ProbeCache _cache;
    private readonly int _parallelism;

    /// <param name="parallelism">Probes run at once. ffprobe on a share is mostly waiting on the network.</param>
    public LibraryScanner(ILibraryDisk disk, IMediaProbe probe, ProbeCache cache, int parallelism = 4)
    {
        _disk = disk;
        _probe = probe;
        _cache = cache;
        _parallelism = Math.Max(1, parallelism);
    }

    /// <summary>
    /// True for folders a scan skips: hidden and system folders (<c>.x</c>, <c>@eaDir</c>,
    /// <c>#recycle</c>, <c>$RECYCLE.BIN</c>) and Plex extras folders.
    /// </summary>
    public static bool IsSkippedFolder(string name) =>
        name.Length == 0 || name[0] is '.' or '@' or '#' or '$' || ExtrasFolders.Contains(name);

    /// <summary>The show folders directly under the root, by name.</summary>
    public IReadOnlyList<string> ListShowFolders(string libraryRoot) =>
        _disk.EnumerateDirectories(libraryRoot)
            .Where(d => !IsSkippedFolder(Path.GetFileName(d)))
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<ShowScan> ScanShowAsync(string showFolder, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        Collect(showFolder, 0, files);

        var records = new EpisodeRecord?[files.Count];
        var options = new ParallelOptions { MaxDegreeOfParallelism = _parallelism, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(Enumerable.Range(0, files.Count), options, async (i, token) =>
            records[i] = await ReadAsync(files[i], token).ConfigureAwait(false)).ConfigureAwait(false);

        var episodes = records
            .OfType<EpisodeRecord>()
            .OrderBy(r => r.Season)
            .ThenBy(r => r.Episode ?? int.MaxValue)
            .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new ShowScan(Path.GetFileName(showFolder.TrimEnd('\\', '/')), showFolder, episodes);
    }

    private void Collect(string folder, int depth, List<string> files)
    {
        files.AddRange(_disk.EnumerateFiles(folder).Where(f => VideoExtensions.Contains(Path.GetExtension(f))));
        if (depth >= MaxDepth)
        {
            return;
        }

        foreach (var sub in _disk.EnumerateDirectories(folder))
        {
            if (!IsSkippedFolder(Path.GetFileName(sub)))
            {
                Collect(sub, depth + 1, files);
            }
        }
    }

    private async Task<EpisodeRecord?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (_disk.Stamp(path) is not { } stamp)
        {
            return null;
        }

        var name = EpisodeName.Parse(path);
        var probe = _cache.Get(path, stamp);
        if (probe is null)
        {
            try
            {
                probe = await ProbeAsync(path, stamp, cancellationToken).ConfigureAwait(false);
                _cache.Put(path, probe);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Not cached: a share that dropped for a moment should get another try next scan.
                probe = new CachedProbe(stamp.Size, stamp.LastWriteUtc, "", false, "Could not probe: " + ex.Message, null, null);
            }
        }

        var code = name.Code.Length > 0 ? name.Code : Path.GetFileNameWithoutExtension(path);
        return new EpisodeRecord(
            path,
            name.Season ?? 0,
            name.Episode,
            code,
            name.Title,
            stamp.Size,
            probe.DurationSeconds,
            probe.FrameRate,
            probe.VideoCodec,
            probe.IsEligible,
            probe.SkipMessage);
    }

    private async Task<CachedProbe> ProbeAsync(string path, FileStamp stamp, CancellationToken cancellationToken)
    {
        var info = await _probe.ProbeAsync(path, cancellationToken).ConfigureAwait(false) with { FileName = path };
        var eligibility = Eligibility.Evaluate(info);
        var video = info.VideoStreams.FirstOrDefault();
        return new CachedProbe(
            stamp.Size,
            stamp.LastWriteUtc,
            (video?.CodecName ?? "").ToLowerInvariant(),
            eligibility.IsEligible,
            eligibility.IsEligible ? "" : eligibility.Message,
            info.DurationSeconds,
            video?.FrameRate);
    }
}
