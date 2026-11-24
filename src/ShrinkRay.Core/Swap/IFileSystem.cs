namespace ShrinkRay.Core.Swap;

/// <summary>Every file operation the engine performs, so tests can run against an in-memory fake.</summary>
public interface IFileSystem
{
    bool FileExists(string path);

    long GetFileSize(string path);

    /// <summary>Renames a file. Never overwrites: throws <see cref="IOException"/> if the destination exists.</summary>
    void MoveFile(string source, string destination);

    void DeleteFile(string path);

    /// <summary>Copies a file. Never overwrites: throws <see cref="IOException"/> if the destination exists.</summary>
    /// <param name="progress">Told the bytes copied so far, now and then and once more at the end.</param>
    Task CopyFileAsync(string source, string destination, IProgress<long>? progress, CancellationToken cancellationToken);

    IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive);

    void CreateDirectory(string path);

    void DeleteDirectory(string path);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    private const int CopyBufferSize = 1024 * 1024;

    /// <summary>How often a copy reports its bytes. The deck redraws no faster than this.</summary>
    public static readonly TimeSpan DefaultProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly TimeSpan _progressInterval;

    /// <param name="progressInterval">The least time between copy progress reports. Tests pass zero.</param>
    public PhysicalFileSystem(TimeSpan? progressInterval = null)
    {
        _progressInterval = progressInterval ?? DefaultProgressInterval;
    }

    public bool FileExists(string path) => File.Exists(path);

    public long GetFileSize(string path) => new FileInfo(path).Length;

    public void MoveFile(string source, string destination) => File.Move(source, destination, overwrite: false);

    public void DeleteFile(string path) => File.Delete(path);

    public async Task CopyFileAsync(string source, string destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);
        // CreateNew fails if the destination exists, so a partial file below is always ours to remove.
        var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);
        try
        {
            await CopyWithProgressAsync(input, output, progress, cancellationToken);
            output.Flush(flushToDisk: true);
            await output.DisposeAsync();
        }
        catch
        {
            await output.DisposeAsync();
            File.Delete(destination);
            throw;
        }
    }

    /// <summary>Stream.CopyToAsync with a byte count, reported at most once per interval and always at the end.</summary>
    private async Task CopyWithProgressAsync(Stream input, Stream output, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[CopyBufferSize];
        var copied = 0L;
        var reported = -1L;
        var sinceReport = System.Diagnostics.Stopwatch.StartNew();
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
            if (progress is not null && sinceReport.Elapsed >= _progressInterval)
            {
                progress.Report(reported = copied);
                sinceReport.Restart();
            }
        }

        if (copied != reported)
        {
            progress?.Report(copied);
        }
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            : [];

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
