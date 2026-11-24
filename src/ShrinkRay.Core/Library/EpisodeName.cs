using System.Globalization;
using System.Text.RegularExpressions;

namespace ShrinkRay.Core.Library;

/// <summary>Season, episode and title read from a file name such as <c>Show - S01E03 - Title.mkv</c>.</summary>
public sealed partial record EpisodeName(int? Season, int? Episode, string Title)
{
    [GeneratedRegex(@"(?<![A-Za-z0-9])[Ss](?<s>\d{1,2})[ ._-]?[Ee](?<e>\d{1,3})(?:-?[Ee]\d{1,3})*", RegexOptions.CultureInvariant)]
    private static partial Regex SxxEyy();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?<s>\d{1,2})x(?<e>\d{2,3})(?![A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex NxNN();

    [GeneratedRegex(@"^(?:Season|Series)\s*(?<s>\d{1,3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonFolder();

    [GeneratedRegex(@"^Specials$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpecialsFolder();

    /// <summary><c>S01E03</c>, or an empty string when the name carries no episode number.</summary>
    public string Code => Season is { } s && Episode is { } e
        ? string.Create(CultureInfo.InvariantCulture, $"S{s:00}E{e:00}")
        : "";

    public static EpisodeName Parse(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var folderSeason = SeasonFromFolder(Path.GetFileName(Path.GetDirectoryName(path) ?? ""));

        var match = SxxEyy().Match(stem);
        if (!match.Success)
        {
            match = NxNN().Match(stem);
        }

        if (!match.Success)
        {
            return new EpisodeName(folderSeason, null, Clean(stem));
        }

        var season = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        var episode = int.Parse(match.Groups["e"].Value, CultureInfo.InvariantCulture);
        var title = Clean(stem[(match.Index + match.Length)..]);
        return new EpisodeName(season, episode, title);
    }

    /// <summary>The season number of a folder such as <c>Season 01</c>; 0 for <c>Specials</c>.</summary>
    public static int? SeasonFromFolder(string folderName)
    {
        var match = SeasonFolder().Match(folderName.Trim());
        if (match.Success)
        {
            return int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        }

        return SpecialsFolder().IsMatch(folderName.Trim()) ? 0 : null;
    }

    // Scene-style names use dots for spaces. Titles keep their own punctuation otherwise.
    private static string Clean(string text)
    {
        var t = text.Trim(' ', '-', '.', '_');
        if (!t.Contains(' ') && t.Contains('.'))
        {
            t = t.Replace('.', ' ');
        }

        return t.Trim(' ', '-', '.', '_');
    }
}
