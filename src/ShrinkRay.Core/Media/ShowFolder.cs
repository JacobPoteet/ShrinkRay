using System.Text.RegularExpressions;

namespace ShrinkRay.Core.Media;

/// <summary>Finds the show folder name for an episode, the key for per-show settings.</summary>
public static partial class ShowFolder
{
    [GeneratedRegex(@"^(Season\s*\d+|Series\s*\d+|Specials|S\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonFolderRegex();

    /// <summary>True for a season folder name such as <c>Season 01</c>, <c>Series 2</c>, <c>S01</c> or <c>Specials</c>.</summary>
    public static bool IsSeasonFolder(string folderName) => SeasonFolderRegex().IsMatch(folderName.Trim());

    /// <summary>
    /// Under the library root, the show is the first folder below the root. When that folder
    /// is a season folder, the root is a single show and names it. Without a root, or outside
    /// it, the show is the episode's folder, or its parent when that folder is a season folder
    /// such as <c>Season 01</c> or <c>Specials</c>.
    /// </summary>
    public static string? NameFor(string episodePath, string? libraryRoot)
    {
        if (!string.IsNullOrWhiteSpace(libraryRoot))
        {
            var trimmed = libraryRoot.TrimEnd('\\', '/');
            var root = trimmed + Path.DirectorySeparatorChar;
            if (episodePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var parts = episodePath[root.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var rootName = Path.GetFileName(trimmed);
                    return IsSeasonFolder(parts[0]) && rootName.Length > 0 ? rootName : parts[0];
                }
            }
        }

        var folder = Path.GetDirectoryName(episodePath);
        if (string.IsNullOrEmpty(folder))
        {
            return null;
        }

        var name = Path.GetFileName(folder);
        if (IsSeasonFolder(name))
        {
            var parent = Path.GetDirectoryName(folder);
            name = string.IsNullOrEmpty(parent) ? "" : Path.GetFileName(parent);
        }

        return string.IsNullOrEmpty(name) ? null : name;
    }
}
