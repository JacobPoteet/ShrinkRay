using System.Text.Json;

namespace ShrinkRay.Core.Library;

/// <summary>The parts of a probe a scan keeps, so an unchanged file is not probed again.</summary>
public sealed record CachedProbe(
    long Size,
    DateTime LastWriteUtc,
    string VideoCodec,
    bool IsEligible,
    string SkipMessage,
    double? DurationSeconds,
    double? FrameRate);

/// <summary>
/// Probe results keyed by path, valid while the file's size and last write time match. Thread
/// safe, since a scan probes several files at once. Saved as JSON by <see cref="Save"/>.
/// </summary>
public sealed class ProbeCache
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private readonly Dictionary<string, CachedProbe> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>The cached probe for <paramref name="path"/>, or null when missing or stale.</summary>
    public CachedProbe? Get(string path, FileStamp stamp)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(path, out var entry) && entry.Size == stamp.Size && entry.LastWriteUtc == stamp.LastWriteUtc
                ? entry
                : null;
        }
    }

    public void Put(string path, CachedProbe probe)
    {
        lock (_gate)
        {
            _entries[path] = probe;
        }
    }

    public void Remove(string path)
    {
        lock (_gate)
        {
            _entries.Remove(path);
        }
    }

    /// <summary>A missing or corrupt file gives an empty cache.</summary>
    public static ProbeCache Load(string path)
    {
        var cache = new ProbeCache();
        try
        {
            if (!File.Exists(path))
            {
                return cache;
            }

            var entries = JsonSerializer.Deserialize<Dictionary<string, CachedProbe>>(File.ReadAllText(path), Options);
            foreach (var (key, value) in entries ?? [])
            {
                if (value is not null && value.VideoCodec is not null && value.SkipMessage is not null)
                {
                    cache._entries[key] = value;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            cache._entries.Clear();
        }

        return cache;
    }

    /// <summary>Writes to a temp file and renames it over the old one.</summary>
    public void Save(string path)
    {
        Dictionary<string, CachedProbe> snapshot;
        lock (_gate)
        {
            snapshot = new Dictionary<string, CachedProbe>(_entries, StringComparer.OrdinalIgnoreCase);
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, Options));
        File.Move(temp, path, overwrite: true);
    }
}
