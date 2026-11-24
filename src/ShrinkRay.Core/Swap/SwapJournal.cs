using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShrinkRay.Core.Swap;

/// <param name="Path">The original file's full path on the share.</param>
public sealed record JournalEntry(DateTimeOffset Timestamp, string Path, SwapState State, string? Reason = null);

/// <summary>Durable storage for journal entries, oldest first.</summary>
public interface IJournalStore
{
    /// <summary>Returns only once the entry is on disk.</summary>
    void Append(JournalEntry entry);

    IReadOnlyList<JournalEntry> ReadAll();
}

/// <summary>Stores the journal as one JSON object per line, flushed to disk on every append.</summary>
public sealed class JsonLinesJournalStore : IJournalStore
{
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

    /// <exception cref="InvalidOperationException">The state machine does not allow this transition.</exception>
    public void Record(string path, SwapState state, string? reason = null)
    {
        lock (_gate)
        {
            var from = _latest.TryGetValue(path, out var current) ? current.State : (SwapState?)null;
            if (!SwapTransitions.IsAllowed(from, state))
            {
                throw new InvalidOperationException($"Cannot move {path} from {from?.ToString() ?? "no state"} to {state}.");
            }

            var entry = new JournalEntry(DateTimeOffset.UtcNow, path, state, reason);
            _store.Append(entry);
            _latest[path] = entry;
        }
    }
}
