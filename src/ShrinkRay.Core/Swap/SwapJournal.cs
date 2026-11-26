using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShrinkRay.Core.Library;

namespace ShrinkRay.Core.Swap;

/// <param name="Path">The original file's full path on the share.</param>
/// <param name="Source">
/// The file as the run read it, on an entry that keeps the original, so a new file under the
/// same name is told apart. Null on other entries and on journals from version 0.1.0.
/// </param>
public sealed record JournalEntry(DateTimeOffset Timestamp, string Path, SwapState State, string? Reason = null, FileStamp? Source = null);

/// <summary>Durable storage for journal entries, oldest first.</summary>
public interface IJournalStore
{
    /// <summary>Returns only once the entry is on disk.</summary>
    void Append(JournalEntry entry);

    IReadOnlyList<JournalEntry> ReadAll();
}

/// <summary>A store that can swap its whole contents for a shorter list in one atomic step.</summary>
public interface ICompactableJournalStore : IJournalStore
{
    /// <summary>
    /// Replaces every entry with <paramref name="entries"/>. A crash at any point leaves either the
    /// old contents or the new ones, never a mix or a partial file.
    /// </summary>
    void ReplaceAll(IReadOnlyList<JournalEntry> entries);
}

/// <summary>Stores the journal as one JSON object per line, flushed to disk on every append.</summary>
public sealed class JsonLinesJournalStore : ICompactableJournalStore
{
    private const string CompactSuffix = ".compact.tmp";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();

    public JsonLinesJournalStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShrinkRay", "journal.jsonl");

    public void Append(JournalEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, Options) + "\n";
        var bytes = Encoding.UTF8.GetBytes(line);
        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
    }

    /// <summary>
    /// Writes the entries to <c>journal.jsonl.compact.tmp</c>, flushes it to disk, then swaps it over
    /// the journal with one atomic replace. A crash before the replace leaves the old journal and
    /// a temp file the next compaction overwrites.
    /// </summary>
    public void ReplaceAll(IReadOnlyList<JournalEntry> entries)
    {
        var text = string.Concat(entries.Select(e => JsonSerializer.Serialize(e, Options) + "\n"));
        var bytes = Encoding.UTF8.GetBytes(text);
        var temp = _path + CompactSuffix;
        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(_path))
                {
                    File.Replace(temp, _path, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(temp, _path);
                }
            }
            catch
            {
                // The old journal is still in place. Remove the half-done temp file if we can.
                try
                {
                    File.Delete(temp);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }

                throw;
            }
        }
    }

    /// <summary>Skips lines that do not parse, such as a line torn by a power cut.</summary>
    public IReadOnlyList<JournalEntry> ReadAll()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var entries = new List<JournalEntry>();
            foreach (var line in File.ReadLines(_path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<JournalEntry>(line, Options);
                    if (entry is not null && !string.IsNullOrEmpty(entry.Path))
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // A torn or corrupt line. The entries around it still count.
                }
            }

            return entries;
        }
    }
}

/// <summary>
/// The per-file state machine over an <see cref="IJournalStore"/>. Every transition is
/// checked and written to disk before the caller runs the step it names.
/// </summary>
public sealed class SwapJournal
{
    private readonly IJournalStore _store;
    private readonly Dictionary<string, JournalEntry> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public SwapJournal(IJournalStore store)
    {
        _store = store;
        foreach (var entry in store.ReadAll())
        {
            _latest[entry.Path] = entry;
        }
    }

    public SwapState? CurrentState(string path)
    {
        lock (_gate)
        {
            return _latest.TryGetValue(path, out var entry) ? entry.State : null;
        }
    }

    public JournalEntry? Latest(string path)
    {
        lock (_gate)
        {
            return _latest.GetValueOrDefault(path);
        }
    }

    /// <summary>The newest entry for every file the journal knows.</summary>
    public IReadOnlyList<JournalEntry> LatestEntries()
    {
        lock (_gate)
        {
            return _latest.Values.ToList();
        }
    }

    /// <summary>
    /// Rewrites the journal with only the newest entry for each file, so it stops growing. Run it
    /// on startup after recovery. Recovery, the queue and the track list read only a file's
    /// latest entry, so nothing they need is lost, whether the state is terminal or not.
    /// A journal with one line per file is left as it is. If the store cannot write (a file held
    /// open, a full disk, no permission) the old journal stays and this returns 0 rather than throwing.
    /// </summary>
    /// <returns>How many lines were dropped.</returns>
    public int Compact()
    {
        try
        {
            return CompactCore();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Housekeeping only. A held, full or locked journal stays as it was and startup goes on.
            return 0;
        }
    }

    private int CompactCore()
    {
        lock (_gate)
        {
            if (_store is not ICompactableJournalStore store)
            {
                return 0;
            }

            var all = store.ReadAll();
            var lastIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < all.Count; i++)
            {
                lastIndex[all[i].Path] = i;
            }

            var kept = all.Where((entry, i) => lastIndex[entry.Path] == i).ToList();
            if (kept.Count == all.Count)
            {
                return 0;
            }

            store.ReplaceAll(kept);
            return all.Count - kept.Count;
        }
    }

    /// <exception cref="InvalidOperationException">The state machine does not allow this transition.</exception>
    public void Record(string path, SwapState state, string? reason = null, FileStamp? source = null)
    {
        lock (_gate)
        {
            var from = _latest.TryGetValue(path, out var current) ? current.State : (SwapState?)null;
            if (!SwapTransitions.IsAllowed(from, state))
            {
                throw new InvalidOperationException($"Cannot move {path} from {from?.ToString() ?? "no state"} to {state}.");
            }

            var entry = new JournalEntry(DateTimeOffset.UtcNow, path, state, reason, source);
            _store.Append(entry);
            _latest[path] = entry;
        }
    }
}
