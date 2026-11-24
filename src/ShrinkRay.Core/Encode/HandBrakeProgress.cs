using System.Globalization;
using System.Text.RegularExpressions;

namespace ShrinkRay.Core.Encode;

/// <param name="Percent">0 to 100.</param>
public sealed record EncodeProgress(int Task, int TaskCount, double Percent, double? Fps, double? AverageFps, TimeSpan? Eta);

/// <summary>
/// Parses HandBrakeCLI's stdout progress, for example
/// <c>Encoding: task 1 of 1, 41.23 % (312.45 fps, avg 298.10 fps, ETA 00h01m10s)</c>.
/// HandBrake ends these lines with a carriage return rather than a newline.
/// </summary>
public static partial class HandBrakeProgressParser
{
    [GeneratedRegex(
        @"Encoding: task (?<task>\d+) of (?<count>\d+), (?<pct>\d+(?:\.\d+)?) %" +
        @"(?: \((?<fps>\d+(?:\.\d+)?) fps)?" +
        @"(?:,? avg (?<avg>\d+(?:\.\d+)?) fps)?" +
        @"(?:,? ETA (?<h>\d+)h(?<m>\d+)m(?<s>\d+)s)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProgressRegex();

    public static bool TryParse(string? line, out EncodeProgress progress)
    {
        progress = null!;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        var match = ProgressRegex().Match(line);
        if (!match.Success)
        {
            return false;
        }

        TimeSpan? eta = null;
        if (match.Groups["h"].Success)
        {
            eta = new TimeSpan(Int(match, "h"), Int(match, "m"), Int(match, "s"));
        }

        progress = new EncodeProgress(
            Int(match, "task"),
            Int(match, "count"),
            Math.Clamp(Double(match, "pct") ?? 0, 0, 100),
            Double(match, "fps"),
            Double(match, "avg"),
            eta);
        return true;
    }

    private static int Int(Match match, string group) =>
        int.Parse(match.Groups[group].Value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static double? Double(Match match, string group) =>
        match.Groups[group].Success
            ? double.Parse(match.Groups[group].Value, NumberStyles.Float, CultureInfo.InvariantCulture)
            : null;
}
