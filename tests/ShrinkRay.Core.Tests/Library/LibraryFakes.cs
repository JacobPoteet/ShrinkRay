using ShrinkRay.Core.Library;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Tests.Support;

namespace ShrinkRay.Core.Tests.Library;

/// <summary>A folder tree in memory. Paths use drive letters such as <c>X:\TV</c>.</summary>
internal sealed class FakeLibraryDisk : ILibraryDisk
{
    private readonly Dictionary<string, FileStamp> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path, long size, DateTime? lastWrite = null)
    {
        _files[path] = new FileStamp(size, lastWrite ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        for (var dir = Path.GetDirectoryName(path); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            _folders.Add(dir);
        }
    }

    public bool DirectoryExists(string path) => _folders.Contains(path.TrimEnd('\\'));

    public IEnumerable<string> EnumerateDirectories(string path) =>
        _folders.Where(f => string.Equals(Path.GetDirectoryName(f), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();

    public IEnumerable<string> EnumerateFiles(string path) =>
        _files.Keys.Where(f => string.Equals(Path.GetDirectoryName(f), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();

    public FileStamp? Stamp(string path) => _files.TryGetValue(path, out var stamp) ? stamp : null;
}

/// <summary>Answers probes from a table and counts the calls.</summary>
internal sealed class CountingProbe : IMediaProbe
{
    public Dictionary<string, MediaInfo> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Calls { get; } = [];

    public Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        lock (Calls)
        {
            Calls.Add(path);
        }

        return Results.TryGetValue(path, out var info)
            ? Task.FromResult(info)
            : throw new IOException("ffprobe could not open the file");
    }

    public static MediaInfo H264(string path, double duration = 1320, double fps = 23.976)
    {
        var info = MediaBuilder.Mkv(path).Duration(duration).Video().Audio("aac").Build();
        return info with
        {
            Streams = info.Streams.Select(s => s.Type == StreamType.Video ? s with { FrameRate = fps } : s).ToList(),
        };
    }

    public static MediaInfo Hevc(string path) => MediaBuilder.Mkv(path).Duration(1320).Video("hevc").Audio("aac").Build();
}
