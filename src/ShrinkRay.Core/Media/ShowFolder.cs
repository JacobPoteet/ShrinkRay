using System.Text.RegularExpressions;

namespace ShrinkRay.Core.Media;

/// <summary>Finds the show folder name for an episode, the key for per-show settings.</summary>
public static partial class ShowFolder
{
    [GeneratedRegex(@"^(Season\s*\d+|Specials|S\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonFolderRegex();

    /// <summary>
    /// Under the library root, the show is the first folder below the root. Without a root, or
    /// outside it, the show is the episode's folder, or its parent when that folder is a season
    /// folder such as <c>Season 01</c> or <c>Specials</c>.
    /// </summary>
    public static string? NameFor(string episodePath, string? libraryRoot)
    {
        if (!string.IsNullOrWhiteSpace(libraryRoot))
        {
            var root = libraryRoot.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (episodePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var parts = episodePath[root.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    return parts[0];
                }
            }
        }

        var folder = Path.GetDirectoryName(episodePath);
        if (string.IsNullOrEmpty(folder))
        {
            return null;
        }

        var name = Path.GetFileName(folder);
        if (SeasonFolderRegex().IsMatch(name))
        {
            var parent = Path.GetDirectoryName(folder);
            name = string.IsNullOrEmpty(parent) ? "" : Path.GetFileName(parent);
        }

        return string.IsNullOrEmpty(name) ? null : name;
    }
}
