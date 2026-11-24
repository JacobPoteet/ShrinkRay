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
    private readonly CrashSwitch _crash = crash ?? new CrashSwitch();

    /// <summary>Throws this once from the next MoveFile whose source matches, like a file Plex holds open.</summary>
    public (string Source, Exception Error)? FailNextMoveFrom { get; set; }

    public IReadOnlyCollection<string> Files => _files.Keys;

    public void Write(string path, string content) => _files[path] = System.Text.Encoding.UTF8.GetBytes(content);

    public string Read(string path) => System.Text.Encoding.UTF8.GetString(_files[path]);

    public bool FileExists(string path) => _files.ContainsKey(path);

    public long GetFileSize(string path) =>
        _files.TryGetValue(path, out var data) ? data.Length : throw new FileNotFoundException(path);

    public void MoveFile(string source, string destination)
    {
        if (FailNextMoveFrom is { } failure && string.Equals(failure.Source, source, StringComparison.OrdinalIgnoreCase))
        {
            FailNextMoveFrom = null;
            throw failure.Error;
        }

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
    }

    public void DeleteFile(string path)
    {
        _crash.Tick($"delete {path}");
        _files.Remove(path);
    }

    public Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
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
            throw new SimulatedCrashException($"copy {source}");
        }

        _files[destination] = data.ToArray();
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

public sealed class InMemoryJournalStore(CrashSwitch? crash = null) : IJournalStore
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
