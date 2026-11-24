using ShrinkRay.Core.Library;
using ShrinkRay.Core.Swap;

namespace ShrinkRay.Core.Tests.Library;

public class ShowSummaryTests
{
    private static EpisodeRecord Episode(int season, int episode, string codec = "h264", long size = 1000, double? fps = 25) =>
        new($@"X:\TV\Show\Season {season:00}\Show - S{season:00}E{episode:00}.mkv", season, episode, $"S{season:00}E{episode:00}", "",
            size, 100, fps, codec, codec == "h264", codec == "h264" ? "" : "Already " + codec);

    [Fact]
    public void Ineligible_files_ShrinkRay_wrote_count_as_shrunk()
    {
        var hevc = Episode(1, 1, "hevc");

        Assert.Equal(EpisodeStatus.Shrunk, EpisodeStatusRules.Classify(hevc, SwapState.OriginalDeleted, inLedger: false));
        Assert.Equal(EpisodeStatus.Shrunk, EpisodeStatusRules.Classify(hevc, null, inLedger: true));
        Assert.Equal(EpisodeStatus.Skipped, EpisodeStatusRules.Classify(hevc, null, inLedger: false));
    }

    [Theory]
    [InlineData(null, EpisodeStatus.Queued)]
    [InlineData(SwapState.KeptOriginal, EpisodeStatus.Kept)]
    [InlineData(SwapState.Failed, EpisodeStatus.Failed)]
    [InlineData(SwapState.OriginalDeleted, EpisodeStatus.Queued)]
    public void Eligible_files_follow_the_journal(SwapState? state, EpisodeStatus expected)
    {
        Assert.Equal(expected, EpisodeStatusRules.Classify(Episode(1, 1), state, inLedger: false));
    }

    [Fact]
    public void Totals_count_queueable_episodes_only()
    {
        var scan = new ShowScan("Show", @"X:\TV\Show", [Episode(1, 1), Episode(1, 2, "hevc", 400), Episode(2, 1, size: 3000)]);

        var summary = ShowSummary.From(scan, e => EpisodeStatusRules.Classify(e, null, false));

        Assert.Equal(2, summary.SeasonCount);
        Assert.Equal(3, summary.EpisodeCount);
        Assert.Equal(4400, summary.TotalBytes);
        Assert.Equal(2, summary.QueueableCount);
        Assert.Equal(4000, summary.QueueableBytes);
        Assert.Equal(5000, summary.QueueableFrames);
        Assert.Equal("MIXED", summary.CodecLabel);
        Assert.True(summary.IsCandidate);
        Assert.Equal(HiddenReason.None, summary.Hidden);
    }

    [Fact]
    public void A_show_with_nothing_left_is_hidden_with_its_reason()
    {
        var scan = new ShowScan("Show", @"X:\TV\Show", [Episode(1, 1, "hevc"), Episode(1, 2, "hevc")]);

        var hevc = ShowSummary.From(scan, e => EpisodeStatusRules.Classify(e, null, false));
        var shrunk = ShowSummary.From(scan, e => EpisodeStatusRules.Classify(e, null, e.Episode == 1));

        Assert.Equal(HiddenReason.Hevc, hevc.Hidden);
        Assert.Equal("HEVC", hevc.CodecLabel);
        Assert.Equal(HiddenReason.Shrunk, shrunk.Hidden);
    }

    [Fact]
    public void Kept_episodes_do_not_keep_a_show_on_the_list()
    {
        var scan = new ShowScan("Show", @"X:\TV\Show", [Episode(1, 1)]);

        var summary = ShowSummary.From(scan, e => EpisodeStatusRules.Classify(e, SwapState.KeptOriginal, false));

        Assert.False(summary.IsCandidate);
        Assert.Equal(HiddenReason.Other, summary.Hidden);
    }

    [Theory]
    [InlineData(22, 0.73)]
    [InlineData(25, 0.505)]
    [InlineData(28, 0.36)]
    [InlineData(20, 0.88)]
    [InlineData(30, 0.2633)]
    public void Size_ratio_follows_the_measured_anchors(int cq, double ratio)
    {
        Assert.Equal(ratio, SizeEstimate.RatioFor(cq), 3);
    }

    [Fact]
    public void Ratio_shrinks_as_cq_rises_and_stays_in_range()
    {
        var ratios = Enumerable.Range(1, 51).Select(SizeEstimate.RatioFor).ToList();

        Assert.All(ratios, r => Assert.InRange(r, 0.15, 0.95));
        Assert.Equal(ratios.OrderByDescending(r => r), ratios);
    }

    [Fact]
    public void Encode_time_comes_from_frames_at_the_measured_speed()
    {
        Assert.Equal(TimeSpan.FromSeconds(200), SizeEstimate.EncodeTime(33_000));
        Assert.Equal(TimeSpan.Zero, SizeEstimate.EncodeTime(0));
    }

    [Fact]
    public void Ledger_keeps_the_latest_entry_per_file_across_reloads()
    {
        var file = Path.Combine(Path.GetTempPath(), "shrinkray-test-" + Guid.NewGuid().ToString("N"), "ledger.jsonl");
        try
        {
            var ledger = new ShrinkLedger(file);
            ledger.Record(new LedgerEntry(@"X:\TV\A.mkv", 1000, 400, 28, DateTimeOffset.UnixEpoch));
            ledger.Record(new LedgerEntry(@"X:\TV\A.mkv", 1000, 350, 28, DateTimeOffset.UnixEpoch));
            File.AppendAllText(file, "{torn");

            var reloaded = new ShrinkLedger(file).Get(@"x:\tv\a.mkv");

            Assert.Equal(650, reloaded?.SavedBytes);
            Assert.Equal(0.65, reloaded!.Savings, 3);
            Assert.NotEqual(0xEF, File.ReadAllBytes(file)[0]);
            Assert.DoesNotContain("savedBytes", File.ReadLines(file).First());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }
}
