using System.Text;
using System.Text.Json;

namespace ShrinkRay.Core.Library;

/// <summary>One episode ShrinkRay replaced: its size before and after.</summary>
public sealed record LedgerEntry(string Path, long OriginalBytes, long NewBytes, int Cq, DateTimeOffset At)
{
    public long SavedBytes => Math.Max(0, OriginalBytes - NewBytes);

    /// <summary>The fraction saved, 0 to 1.</summary>
    public double Savings => OriginalBytes > 0 ? 1.0 - (double)NewBytes / OriginalBytes : 0;
}

/// <summary>
/// Remembers what each replaced episode weighed before, so the deck can show the space a show
/// has given back. Kept as JSON lines; pass no path for an in-memory ledger.
/// </summary>
public sealed class ShrinkLedger
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string? _path;
    private readonly Dictionary<string, LedgerEntry> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public ShrinkLedger(string? path = null)
    {
        _path = path;
        if (path is null || !File.Exists(path))
        {
            return;
        }

        try
        {
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    if (JsonSerializer.Deserialize<LedgerEntry>(line, Options) is { Path.Length: > 0 } entry)
                    {
                        _latest[entry.Path] = entry;
                    }
                }
                catch (JsonException)
                {
                    // A torn line. The rest still count.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _latest.Clear();
        }
    }

    public LedgerEntry? Get(string path)
    {
        lock (_gate)
        {
            return _latest.GetValueOrDefault(path);
        }
    }

    public void Record(LedgerEntry entry)
    {
        lock (_gate)
        {
            _latest[entry.Path] = entry;
            if (_path is null)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(_path, JsonSerializer.Serialize(entry, Options) + "\n", Encoding.UTF8);
        }
    }
}
