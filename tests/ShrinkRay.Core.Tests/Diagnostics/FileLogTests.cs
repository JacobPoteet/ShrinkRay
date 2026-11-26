using ShrinkRay.Core.Diagnostics;

namespace ShrinkRay.Core.Tests.Diagnostics;

public sealed class FileLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "shrinkray-log-" + Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_folder, "shrinkray.log");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private FileLog Log(long maxBytes = FileLog.DefaultMaxBytes) =>
        new(LogPath, maxBytes, () => new DateTimeOffset(2026, 11, 26, 1, 2, 3, TimeSpan.Zero));

    [Fact]
    public void Entries_are_stamped_and_their_later_lines_indented()
    {
        var log = Log();

        log.Write("Run: Scrubs");
        log.Write("ffmpeg ended with code 1\r\nfirst\nsecond\n");

        Assert.Equal(
            [
                "2026-11-26 01:02:03  Run: Scrubs",
                "2026-11-26 01:02:03  ffmpeg ended with code 1",
                "    first",
                "    second",
            ],
            File.ReadAllLines(LogPath));
    }

    [Fact]
    public void A_full_log_moves_aside_and_replaces_the_one_before()
    {
        var log = Log(maxBytes: 20);

        log.Write("one");
        log.Write("two");
        log.Write("three");

        Assert.Contains("three", File.ReadAllText(LogPath));
        Assert.Contains("two", File.ReadAllText(log.OldPath));
        Assert.DoesNotContain("one", File.ReadAllText(log.OldPath));
    }

    [Fact]
    public void A_log_that_cannot_be_written_is_ignored()
    {
        Directory.CreateDirectory(LogPath);

        var failure = Record.Exception(() => Log().Write("anything"));

        Assert.Null(failure);
    }
}
