using ShrinkRay.Core.Library;
using ShrinkRay.Core.Swap;

namespace ShrinkRay.Core.Tests.Support;

public sealed class SimulatedCrashException(string step) : Exception($"Simulated crash at {step}");

/// <summary>
/// Counts durable operations across the fake file system and journal. After the set number of
/// operations the next one "loses power": it throws, and so does every operation after it until
/// <see cref="Restart"/>, the way a dead process would do nothing more.
/// </summary>
public sealed class CrashSwitch
{
    private int? _crashAfter;
    private bool _down;

    public int Operations { get; private set; }

    public void CrashAfter(int operations)
    {
        _crashAfter = operations;
        Operations = 0;
    }

    public void Restart()
    {
        _crashAfter = null;
        _down = false;
    }

    /// <summary>True if this operation should not complete.</summary>
    public bool Fails()
    {
        if (_down)
        {
            return true;
        }

        if (_crashAfter is { } limit && Operations >= limit)
        {
            _down = true;
            return true;
        }

        Operations++;
        return false;
    }

    public void Tick(string step)
    {
        if (Fails())
        {
            throw new SimulatedCrashException(step);
        }
    }
}

public sealed class InMemoryFileSystem(CrashSwitch? crash = null) : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _written = new(StringComparer.OrdinalIgnoreCase);
    private readonly CrashSwitch _crash = crash ?? new CrashSwitch();
    private int _writes;

    /// <summary>Throws this once from the next MoveFile whose source matches, like a file Plex holds open.</summary>
    public (string Source, Exception Error)? FailNextMoveFrom { get; set; }

    /// <summary>Runs just before a move, such as another program writing to the file about to be renamed.</summary>
    public Action<string, string>? BeforeMove { get; set; }

    public IReadOnlyCollection<string> Files => _files.Keys;

    /// <summary>Writes a file as another program would, with a write time later than any before it.</summary>
    public void Write(string path, string content)
    {
        _files[path] = System.Text.Encoding.UTF8.GetBytes(content);
        Touch(path);
    }

    public string Read(string path) => System.Text.Encoding.UTF8.GetString(_files[path]);

    public bool FileExists(string path) => _files.ContainsKey(path);

    public long GetFileSize(string path) =>
        _files.TryGetValue(path, out var data) ? data.Length : throw new FileNotFoundException(path);

    public FileStamp GetStamp(string path) => new(GetFileSize(path), _written[path]);

    private void Touch(string path) => _written[path] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(++_writes);

    public void MoveFile(string source, string destination)
    {
        if (FailNextMoveFrom is { } failure && string.Equals(failure.Source, source, StringComparison.OrdinalIgnoreCase))
        {
            FailNextMoveFrom = null;
            throw failure.Error;
        }

        BeforeMove?.Invoke(source, destination);
        _crash.Tick($"move {source}");
        if (!_files.Remove(source, out var data))
        {
            throw new FileNotFoundException(source);
        }

        if (_files.ContainsKey(destination))
        {
            _files[source] = data;
            throw new IOException($"{destination} already exists");
        }

        _files[destination] = data;

        // A rename keeps the write time.
        _written[destination] = _written[source];
    }

    public void DeleteFile(string path)
    {
        _crash.Tick($"delete {path}");
        _files.Remove(path);
    }

    public Task CopyFileAsync(string source, string destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_files.TryGetValue(source, out var data))
        {
            throw new FileNotFoundException(source);
        }

        if (_files.ContainsKey(destination))
        {
            throw new IOException($"{destination} already exists");
        }

        if (_crash.Fails())
        {
            // Power lost halfway through the copy.
            _files[destination] = data[..(data.Length / 2)];
            Touch(destination);
            throw new SimulatedCrashException($"copy {source}");
        }

        _files[destination] = data.ToArray();
        Touch(destination);
        progress?.Report(data.Length);
        return Task.CompletedTask;
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive)
    {
        var prefix = directory.TrimEnd('\\') + "\\";
        var suffix = searchPattern.TrimStart('*');
        return _files.Keys
            .Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(f => recursive || !f[prefix.Length..].Contains('\\'))
            .Where(f => f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void CreateDirectory(string path)
    {
        _crash.Tick($"mkdir {path}");
        _directories.Add(path);
    }

    public void DeleteDirectory(string path)
    {
        _crash.Tick($"rmdir {path}");
        var prefix = path.TrimEnd('\\') + "\\";
        foreach (var file in _files.Keys.Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _files.Remove(file);
        }

        _directories.Remove(path);
    }
}

public sealed class InMemoryJournalStore(CrashSwitch? crash = null) : ICompactableJournalStore
{
    private readonly List<JournalEntry> _entries = [];
    private readonly CrashSwitch _crash = crash ?? new CrashSwitch();

    public IReadOnlyList<JournalEntry> Entries => _entries;

    public void Append(JournalEntry entry)
    {
        _crash.Tick($"journal {entry.State}");
        _entries.Add(entry);
    }

    public IReadOnlyList<JournalEntry> ReadAll() => _entries.ToList();

    /// <summary>Two durable operations: the new file is written beside the journal, then swapped over it.</summary>
    public void ReplaceAll(IReadOnlyList<JournalEntry> entries)
    {
        _crash.Tick("journal compact write");
        _crash.Tick("journal compact replace");
        _entries.Clear();
        _entries.AddRange(entries);
    }

    public IEnumerable<SwapState> StatesFor(string path) =>
        _entries.Where(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)).Select(e => e.State);
}

public sealed class FakeFinalCheck(bool result = true) : IFinalFileCheck
{
    public bool Result { get; set; } = result;
    public int Calls { get; private set; }

    public Task<bool> VerifiesAsync(string finalPath, string backupPath, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}
