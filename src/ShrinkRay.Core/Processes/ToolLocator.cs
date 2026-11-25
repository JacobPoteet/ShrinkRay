namespace ShrinkRay.Core.Processes;

/// <summary>
/// Finds the external tools. A full path in settings wins; a bare name is looked for on PATH
/// and then in the folders winget installs portable packages to, where HandBrakeCLI lands
/// without joining PATH. It reads the saved PATH as well as the process's own, so a tool
/// installed while ShrinkRay runs is found without a restart.
/// </summary>
public static class ToolLocator
{
    /// <summary>The winget package folder prefix for HandBrakeCLI.</summary>
    public const string HandBrakeWingetPackage = "HandBrake.HandBrake.CLI_";

    /// <param name="configured">The path or bare file name from settings.</param>
    /// <param name="pathVariable">The PATH environment variable.</param>
    /// <param name="extraFolders">Folders to try after PATH.</param>
    /// <returns>The full path of the tool, or null when it is nowhere to be found.</returns>
    public static string? Resolve(string configured, string? pathVariable, IEnumerable<string> extraFolders, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var name = configured.Trim();
        if (Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            return fileExists(name) ? name : null;
        }

        var folders = (pathVariable ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.Trim('"'))
            .Concat(extraFolders);
        foreach (var folder in folders)
        {
            if (folder.Length == 0)
            {
                continue;
            }

            var candidate = Path.Combine(folder, name);
            if (fileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The winget package folders for HandBrakeCLI under <c>%LOCALAPPDATA%</c>.</summary>
    public static IEnumerable<string> WingetFolders(string localAppData, Func<string, IEnumerable<string>> listDirectories)
    {
        var packages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        return listDirectories(packages)
            .Where(d => Path.GetFileName(d).StartsWith(HandBrakeWingetPackage, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The folder winget links portable commands into, such as ffmpeg.</summary>
    public static string WingetLinks(string localAppData) => Path.Combine(localAppData, "Microsoft", "WinGet", "Links");

    /// <summary>
    /// One PATH from several, in order. A running process keeps the PATH it started with, so the
    /// saved user and machine values go after it. Saved values may name variables, which expand.
    /// </summary>
    public static string JoinPaths(params string?[] paths) =>
        string.Join(Path.PathSeparator, paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Environment.ExpandEnvironmentVariables(p!)));

    /// <summary>Resolves against this machine's PATH and winget folders.</summary>
    public static string? ResolveOnThisMachine(string configured)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var winget = WingetFolders(localAppData, dir =>
        {
            try
            {
                return Directory.Exists(dir) ? Directory.GetDirectories(dir) : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        });
        var path = JoinPaths(
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
        return Resolve(configured, path, winget.Append(WingetLinks(localAppData)), File.Exists);
    }
}
