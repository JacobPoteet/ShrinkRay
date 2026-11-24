using ShrinkRay.Core.Library;

namespace ShrinkRay.Core.Tests.Library;

public class LibraryScannerTests
{
    private const string Root = @"X:\TV";

    private static (FakeLibraryDisk Disk, CountingProbe Probe, ProbeCache Cache, LibraryScanner Scanner) Rig()
    {
        var disk = new FakeLibraryDisk();
        var probe = new CountingProbe();
        var cache = new ProbeCache();
        return (disk, probe, cache, new LibraryScanner(disk, probe, cache, parallelism: 2));
    }

    private static string Ep(string show, int season, int episode, string ext = ".mkv") =>
        $@"{Root}\{show}\Season {season:00}\{show} - S{season:00}E{episode:00}{ext}";

    [Fact]
    public void Lists_show_folders_by_name_and_skips_system_folders()
    {
        var (disk, _, _, scanner) = Rig();
        disk.AddFile(Ep("Zed Show", 1, 1), 10);
        disk.AddFile(Ep("alpha show", 1, 1), 10);
        disk.AddFile($@"{Root}\@eaDir\thumb.mkv", 10);
        disk.AddFile($@"{Root}\#recycle\old.mkv", 10);
        disk.AddFile($@"{Root}\.hidden\x.mkv", 10);

        var shows = scanner.ListShowFolders(Root).Select(Path.GetFileName);

        Assert.Equal(["alpha show", "Zed Show"], shows);
    }

    [Fact]
    public async Task Scans_episodes_in_season_order_with_probe_details()
    {
        var (disk, probe, _, scanner) = Rig();
        foreach (var (s, e) in new[] { (2, 1), (1, 2), (1, 1) })
        {
            disk.AddFile(Ep("Show", s, e), 1000 * e);
            probe.Results[Ep("Show", s, e)] = CountingProbe.H264(Ep("Show", s, e), duration: 600, fps: 25);
        }

        var scan = await scanner.ScanShowAsync($@"{Root}\Show", default);

        Assert.Equal("Show", scan.Name);
        Assert.Equal(["S01E01", "S01E02", "S02E01"], scan.Episodes.Select(e => e.Code));
        var first = scan.Episodes[0];
        Assert.True(first.IsEligible);
        Assert.Equal("h264", first.VideoCodec);
        Assert.Equal(1000, first.SizeBytes);
        Assert.Equal(15000, first.FrameCount);
    }

    [Fact]
    public async Task Marks_hevc_files_ineligible_and_skips_extras_and_non_video()
    {
        var (disk, probe, _, scanner) = Rig();
        disk.AddFile(Ep("Show", 1, 1), 100);
        probe.Results[Ep("Show", 1, 1)] = CountingProbe.Hevc(Ep("Show", 1, 1));
        disk.AddFile($@"{Root}\Show\Season 01\Show - S01E01.srt", 1);
        disk.AddFile($@"{Root}\Show\Featurettes\Making of.mkv", 1);
        disk.AddFile($@"{Root}\Show\Season 01\Show - S01E02.mkv.shrinkray.tmp", 1);

        var scan = await scanner.ScanShowAsync($@"{Root}\Show", default);

        var only = Assert.Single(scan.Episodes);
        Assert.False(only.IsEligible);
        Assert.True(only.IsEfficientCodec);
        Assert.Equal("Already hevc", only.SkipMessage);
    }

    [Fact]
    public async Task Reuses_cached_probes_until_the_file_changes()
    {
        var (disk, probe, cache, scanner) = Rig();
        var path = Ep("Show", 1, 1);
        disk.AddFile(path, 100);
        probe.Results[path] = CountingProbe.H264(path);

        await scanner.ScanShowAsync($@"{Root}\Show", default);
        await scanner.ScanShowAsync($@"{Root}\Show", default);
        Assert.Single(probe.Calls);
        Assert.Equal(1, cache.Count);

        disk.AddFile(path, 60, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        probe.Results[path] = CountingProbe.Hevc(path);
        var scan = await scanner.ScanShowAsync($@"{Root}\Show", default);

        Assert.Equal(2, probe.Calls.Count);
        Assert.False(scan.Episodes[0].IsEligible);
    }

    [Fact]
    public async Task A_failed_probe_is_skipped_and_not_cached()
    {
        var (disk, probe, cache, scanner) = Rig();
        var path = Ep("Show", 1, 1);
        disk.AddFile(path, 100);

        var scan = await scanner.ScanShowAsync($@"{Root}\Show", default);

        var episode = Assert.Single(scan.Episodes);
        Assert.False(episode.IsEligible);
        Assert.StartsWith("Could not probe", episode.SkipMessage);
        Assert.Equal(0, cache.Count);
        Assert.Single(probe.Calls);
    }

    [Fact]
    public void Probe_cache_survives_a_save_and_load()
    {
        var file = Path.Combine(Path.GetTempPath(), "shrinkray-test-" + Guid.NewGuid().ToString("N"), "cache.json");
        try
        {
            var cache = new ProbeCache();
            var stamp = new FileStamp(100, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            cache.Put(@"X:\TV\A.mkv", new CachedProbe(stamp.Size, stamp.LastWriteUtc, "h264", true, "", 1320, 23.976));
            cache.Save(file);

            var loaded = ProbeCache.Load(file);

            Assert.Equal("h264", loaded.Get(@"x:\tv\a.mkv", stamp)?.VideoCodec);
            Assert.Null(loaded.Get(@"X:\TV\A.mkv", stamp with { Size = 101 }));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }

    [Fact]
    public void A_corrupt_cache_file_loads_empty()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "{ not json");

            Assert.Equal(0, ProbeCache.Load(file).Count);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
