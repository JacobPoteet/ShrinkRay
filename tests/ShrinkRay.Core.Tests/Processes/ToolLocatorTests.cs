using ShrinkRay.Core.Processes;

namespace ShrinkRay.Core.Tests.Processes;

public class ToolLocatorTests
{
    private static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Tools\ffprobe.exe",
        @"C:\Users\u\AppData\Local\Microsoft\WinGet\Packages\HandBrake.HandBrake.CLI_Microsoft.Winget.Source_abc\HandBrakeCLI.exe",
        @"D:\Portable\HandBrakeCLI.exe",
    };

    [Fact]
    public void Finds_a_bare_name_on_path()
    {
        Assert.Equal(@"C:\Tools\ffprobe.exe", ToolLocator.Resolve("ffprobe.exe", @"C:\Windows;""C:\Tools""", [], Files.Contains));
    }

    [Fact]
    public void Falls_back_to_the_winget_package_folder()
    {
        var winget = ToolLocator.WingetFolders(@"C:\Users\u\AppData\Local", dir =>
            dir.EndsWith(@"WinGet\Packages", StringComparison.OrdinalIgnoreCase)
                ? [dir + @"\Other.Package_x", dir + @"\HandBrake.HandBrake.CLI_Microsoft.Winget.Source_abc"]
                : []);

        var found = ToolLocator.Resolve("HandBrakeCLI.exe", @"C:\Windows", winget, Files.Contains);

        Assert.EndsWith(@"HandBrake.HandBrake.CLI_Microsoft.Winget.Source_abc\HandBrakeCLI.exe", found);
    }

    [Fact]
    public void A_full_path_is_used_as_given_or_not_at_all()
    {
        Assert.Equal(@"D:\Portable\HandBrakeCLI.exe", ToolLocator.Resolve(@"D:\Portable\HandBrakeCLI.exe", "", [], Files.Contains));
        Assert.Null(ToolLocator.Resolve(@"E:\Missing\HandBrakeCLI.exe", @"D:\Portable", [], Files.Contains));
    }

    [Fact]
    public void A_tool_on_the_saved_path_only_is_found_without_a_restart()
    {
        var path = ToolLocator.JoinPaths(@"C:\Windows", null, "", @"C:\Tools");

        Assert.Equal(@"C:\Tools\ffprobe.exe", ToolLocator.Resolve("ffprobe.exe", path, [], Files.Contains));
    }

    [Fact]
    public void Saved_paths_expand_their_variables()
    {
        var windows = Environment.GetEnvironmentVariable("SystemRoot")!;

        Assert.Equal(windows + @"\System32", ToolLocator.JoinPaths(@"%SystemRoot%\System32"));
    }

    [Fact]
    public void Winget_links_its_commands_under_local_appdata() =>
        Assert.Equal(@"C:\Users\u\AppData\Local\Microsoft\WinGet\Links", ToolLocator.WingetLinks(@"C:\Users\u\AppData\Local"));

    [Fact]
    public void Missing_everywhere_gives_null()
    {
        Assert.Null(ToolLocator.Resolve("ffmpeg.exe", @"C:\Tools", [], Files.Contains));
        Assert.Null(ToolLocator.Resolve(" ", @"C:\Tools", [], Files.Contains));
    }
}
