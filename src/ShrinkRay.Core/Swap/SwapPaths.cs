namespace ShrinkRay.Core.Swap;

/// <summary>Names of the files that sit beside an original during a swap.</summary>
public static class SwapPaths
{
    public const string TempSuffix = ".shrinkray.tmp";
    public const string BackupSuffix = ".shrinkray.bak";

    public static string Temp(string originalPath) => originalPath + TempSuffix;

    public static string Backup(string originalPath) => originalPath + BackupSuffix;

    /// <summary>The original path for a temp or backup path, or null if it is neither.</summary>
    public static string? OriginalFor(string path)
    {
        foreach (var suffix in new[] { TempSuffix, BackupSuffix })
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && path.Length > suffix.Length)
            {
                return path[..^suffix.Length];
            }
        }

        return null;
    }
}
