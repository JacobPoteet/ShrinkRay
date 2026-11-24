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
    Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken);

    IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive);

    void CreateDirectory(string path);

    void DeleteDirectory(string path);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    private const int CopyBufferSize = 1024 * 1024;

    public bool FileExists(string path) => File.Exists(path);

    public long GetFileSize(string path) => new FileInfo(path).Length;

    public void MoveFile(string source, string destination) => File.Move(source, destination, overwrite: false);

    public void DeleteFile(string path) => File.Delete(path);

    public async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);
        // CreateNew fails if the destination exists, so a partial file below is always ours to remove.
        var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);
        try
        {
            await input.CopyToAsync(output, CopyBufferSize, cancellationToken);
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
