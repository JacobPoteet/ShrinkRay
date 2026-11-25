namespace ShrinkRay.Core.Setup;

/// <summary>Whether a folder will do for scratch.</summary>
/// <param name="FreeBytes">Free space on its drive, or null when the drive does not say.</param>
/// <param name="NeededBytes">Twice the largest episode: room for the copy and the encode, with a margin.</param>
/// <param name="IsOnNetwork">The folder is on a share.</param>
/// <param name="IsInLibrary">The folder is the library or sits inside it.</param>
public sealed record ScratchCheck(long? FreeBytes, long NeededBytes, bool IsOnNetwork, bool IsInLibrary)
{
    public bool IsLowOnSpace => FreeBytes is { } free && free < NeededBytes;

    /// <summary>
    /// Encodes are built and checked on a local disk, and the library only receives finished
    /// files, so a folder on a share or inside the library is refused. Low space only warns.
    /// </summary>
    public bool IsUsable => !IsOnNetwork && !IsInLibrary;

    public static ScratchCheck For(string folder, string libraryRoot, long largestEpisodeBytes, long? freeBytes, bool onNetwork) =>
        new(freeBytes, largestEpisodeBytes * 2, onNetwork || IsNetworkShare(folder), IsInside(folder, libraryRoot));

    /// <summary>True for a path that names a server, such as <c>\\server\share</c>. A mapped drive letter does not show here.</summary>
    public static bool IsNetworkShare(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal)
        || path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string folder, string root)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        static string WithSlash(string p) => p.Trim().Replace('/', '\\').TrimEnd('\\') + "\\";
        return WithSlash(folder).StartsWith(WithSlash(root), StringComparison.OrdinalIgnoreCase);
    }
}
