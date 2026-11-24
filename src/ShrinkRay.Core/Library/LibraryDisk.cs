namespace ShrinkRay.Core.Library;

/// <summary>Size and last write time, which together tell whether a cached probe still holds.</summary>
public readonly record struct FileStamp(long Size, DateTime LastWriteUtc);

/// <summary>The read-only folder walking a library scan needs, so tests can run against a fake.</summary>
public interface ILibraryDisk
{
    bool DirectoryExists(string path);

    /// <summary>Subfolders, one level down. Empty when the folder is missing or unreadable.</summary>
    IEnumerable<string> EnumerateDirectories(string path);

    /// <summary>Files, one level down. Empty when the folder is missing or unreadable.</summary>
    IEnumerable<string> EnumerateFiles(string path);

    /// <summary>Null when the file is missing or unreadable.</summary>
    FileStamp? Stamp(string path);
}

public sealed class PhysicalLibraryDisk : ILibraryDisk
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IEnumerable<string> EnumerateDirectories(string path) => Safely(() => Directory.GetDirectories(path));

    public IEnumerable<string> EnumerateFiles(string path) => Safely(() => Directory.GetFiles(path));

    public FileStamp? Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string[] Safely(Func<string[]> list)
    {
        try
        {
            return list();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
