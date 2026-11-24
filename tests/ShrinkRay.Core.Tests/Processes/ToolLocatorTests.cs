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
    public void Missing_everywhere_gives_null()
    {
        Assert.Null(ToolLocator.Resolve("ffmpeg.exe", @"C:\Tools", [], Files.Contains));
        Assert.Null(ToolLocator.Resolve(" ", @"C:\Tools", [], Files.Contains));
    }
}
