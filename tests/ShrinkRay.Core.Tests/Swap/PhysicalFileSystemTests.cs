using ShrinkRay.Core.Swap;

namespace ShrinkRay.Core.Tests.Swap;

/// <summary>The real file copy, in a temp folder of its own.</summary>
public sealed class PhysicalFileSystemTests : IDisposable
{
    // Three and a half copy buffers, so the loop runs four times and ends on a short read.
    private const int Size = (3 * 1024 * 1024) + (512 * 1024);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "shrinkray-fs-" + Guid.NewGuid().ToString("N"));

    public PhysicalFileSystemTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    private sealed class ListProgress : IProgress<long>
    {
        public List<long> Reports { get; } = [];

        public void Report(long value) => Reports.Add(value);
    }

    private string Source()
    {
        var path = Path.Combine(_folder, "source.mkv");
        var bytes = new byte[Size];
        new Random(19).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void A_stamp_survives_a_rename_and_changes_when_the_file_is_written()
    {
        var fs = new PhysicalFileSystem();
        var source = Source();
        File.SetLastWriteTimeUtc(source, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var stamp = fs.GetStamp(source);
        var renamed = Path.Combine(_folder, "source.mkv.shrinkray.bak");

        fs.MoveFile(source, renamed);

        Assert.Equal(stamp, fs.GetStamp(renamed));
        Assert.Equal(Size, stamp.Size);

        // Rewritten at the same size, as an upgrade of the same length would be.
        File.WriteAllBytes(renamed, new byte[Size]);
        Assert.NotEqual(stamp, fs.GetStamp(renamed));
        Assert.Throws<FileNotFoundException>(() => fs.GetStamp(source));
    }

    [Fact]
    public async Task Copy_reports_the_bytes_as_they_move_and_ends_on_the_full_size()
    {
        var source = Source();
        var destination = Path.Combine(_folder, "copy.mkv");
        var progress = new ListProgress();

        await new PhysicalFileSystem(TimeSpan.Zero).CopyFileAsync(source, destination, progress, CancellationToken.None);

        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(destination));
        Assert.True(progress.Reports.Count >= 4, $"Only {progress.Reports.Count} reports.");
        Assert.Equal(progress.Reports.Order().Distinct(), progress.Reports);
        Assert.True(progress.Reports[0] < Size);
        Assert.Equal(Size, progress.Reports[^1]);
    }

    [Fact]
    public async Task A_throttled_copy_still_reports_the_end()
    {
        var source = Source();
        var progress = new ListProgress();

        await new PhysicalFileSystem(TimeSpan.FromHours(1)).CopyFileAsync(source, Path.Combine(_folder, "copy.mkv"), progress, CancellationToken.None);

        Assert.Equal([Size], progress.Reports);
    }

    [Fact]
    public async Task A_cancelled_copy_removes_its_partial_file()
    {
        var source = Source();
        var destination = Path.Combine(_folder, "copy.mkv");
        using var cts = new CancellationTokenSource();
        var progress = new CancelOnFirstReport(cts);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PhysicalFileSystem(TimeSpan.Zero).CopyFileAsync(source, destination, progress, cts.Token));

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Copy_never_overwrites()
    {
        var source = Source();
        var destination = Path.Combine(_folder, "copy.mkv");
        File.WriteAllText(destination, "keep me");

        await Assert.ThrowsAsync<IOException>(() =>
            new PhysicalFileSystem().CopyFileAsync(source, destination, null, CancellationToken.None));

        Assert.Equal("keep me", File.ReadAllText(destination));
    }

    private sealed class CancelOnFirstReport(CancellationTokenSource cts) : IProgress<long>
    {
        public void Report(long value) => cts.Cancel();
    }
}
