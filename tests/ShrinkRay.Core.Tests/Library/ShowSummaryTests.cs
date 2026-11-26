using ShrinkRay.Core.Library;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Swap;

namespace ShrinkRay.Core.Tests.Library;

public class ShowSummaryTests
{
    private static EpisodeRecord Episode(int season, int episode, string codec = "h264", long size = 1000, double? fps = 25) =>
        new($@"X:\TV\Show\Season {season:00}\Show - S{season:00}E{episode:00}.mkv", season, episode, $"S{season:00}E{episode:00}", "",
            size, 100, fps, codec, codec == "h264", codec == "h264" ? "" : "Already " + codec, Written);

    private static readonly DateTime Written = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Decided = new(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);

    private static JournalEntry Kept(string reason = "Saves 9.0%, below the 15.0% minimum", FileStamp? source = null) =>
        new(Decided, "path", SwapState.KeptOriginal, reason, source);

    private static EpisodeStatus Classify(EpisodeRecord episode, SwapState? state = null, bool inLedger = false) =>
        EpisodeStatusRules.Classify(episode, state is { } s ? new JournalEntry(Decided, episode.Path, s) : null, inLedger, true);

    [Fact]
    public void A_kept_original_stays_kept_while_the_same_file_holds_the_name()
    {
        var kept = Kept(source: new FileStamp(1000, Written));

        Assert.Equal(EpisodeStatus.Kept, EpisodeStatusRules.Classify(Episode(1, 1), kept, false, true));
    }

    [Theory]
    [InlineData(2000, 0)] // Another size.
    [InlineData(1000, 60)] // The same size, written again.
    public void A_new_file_under_a_kept_name_queues_again(long size, int minutes)
    {
        var kept = Kept(source: new FileStamp(1000, Written));
        var now = Episode(1, 1, size: size) with { ModifiedUtc = Written.AddMinutes(minutes) };

        Assert.Equal(EpisodeStatus.Queued, EpisodeStatusRules.Classify(now, kept, false, true));
    }

    [Fact]
    public void An_original_kept_because_replacing_was_off_queues_once_replacing_is_on()
    {
        var kept = Kept(EpisodePipeline.ReplaceOffReason + @" The verified encode is at C:\Scratch\x.mkv.", new FileStamp(1000, Written));

        Assert.Equal(EpisodeStatus.Kept, EpisodeStatusRules.Classify(Episode(1, 1), kept, false, replacingOriginals: false));
        Assert.Equal(EpisodeStatus.Queued, EpisodeStatusRules.Classify(Episode(1, 1), kept, false, replacingOriginals: true));
    }

    [Fact]
    public void An_original_kept_for_saving_too_little_stays_kept_whether_replacing_is_on_or_off()
    {
        var kept = Kept(source: new FileStamp(1000, Written));

        Assert.Equal(EpisodeStatus.Kept, EpisodeStatusRules.Classify(Episode(1, 1), kept, false, false));
        Assert.Equal(EpisodeStatus.Kept, EpisodeStatusRules.Classify(Episode(1, 1), kept, false, true));
    }

    [Theory]
    [InlineData(-1, EpisodeStatus.Kept)]
    [InlineData(1, EpisodeStatus.Queued)]
    public void A_journal_without_a_stamp_compares_the_files_write_time(int daysAfterTheDecision, EpisodeStatus expected)
    {
        var now = Episode(1, 1) with { ModifiedUtc = Decided.UtcDateTime.AddDays(daysAfterTheDecision) };

        Assert.Equal(expected, EpisodeStatusRules.Classify(now, Kept(), false, true));
    }

    [Fact]
    public void Ineligible_files_ShrinkRay_wrote_count_as_shrunk()
    {
        var hevc = Episode(1, 1, "hevc");

        Assert.Equal(EpisodeStatus.Shrunk, Classify(hevc, SwapState.OriginalDeleted));
        Assert.Equal(EpisodeStatus.Shrunk, Classify(hevc, inLedger: true));
        Assert.Equal(EpisodeStatus.Skipped, Classify(hevc));
    }

    [Theory]
    [InlineData(null, EpisodeStatus.Queued)]
    [InlineData(SwapState.KeptOriginal, EpisodeStatus.Kept)]
    [InlineData(SwapState.Failed, EpisodeStatus.Failed)]
    [InlineData(SwapState.Cancelled, EpisodeStatus.Queued)]
    [InlineData(SwapState.OriginalDeleted, EpisodeStatus.Queued)]
    public void Eligible_files_follow_the_journal(SwapState? state, EpisodeStatus expected)
    {
        Assert.Equal(expected, Classify(Episode(1, 1), state));
    }

    [Fact]
    public void Totals_count_queueable_episodes_only()
    {
        var scan = new ShowScan("Show", @"X:\TV\Show", [Episode(1, 1), Episode(1, 2, "hevc", 400), Episode(2, 1, size: 3000)]);

        var summary = ShowSummary.From(scan, e => Classify(e));

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

        var hevc = ShowSummary.From(scan, e => Classify(e));
        var shrunk = ShowSummary.From(scan, e => Classify(e, inLedger: e.Episode == 1));

        Assert.Equal(HiddenReason.Hevc, hevc.Hidden);
        Assert.Equal("HEVC", hevc.CodecLabel);
        Assert.Equal(HiddenReason.Shrunk, shrunk.Hidden);
    }

    [Fact]
    public void Kept_episodes_do_not_keep_a_show_on_the_list()
    {
        var scan = new ShowScan("Show", @"X:\TV\Show", [Episode(1, 1)]);

        var summary = ShowSummary.From(scan, e => Classify(e, SwapState.KeptOriginal));

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
    public void Ledger_total_saved_counts_each_file_once_at_its_latest_entry()
    {
        var ledger = new ShrinkLedger();
        Assert.Equal(0, ledger.TotalSaved());

        ledger.Record(new LedgerEntry(@"X:\TV\A.mkv", 1000, 400, 28, DateTimeOffset.UnixEpoch));
        ledger.Record(new LedgerEntry(@"X:\TV\B.mkv", 500, 300, 28, DateTimeOffset.UnixEpoch));
        Assert.Equal(800, ledger.TotalSaved());

        // A re-encode of the same file replaces its entry, and a file that grew counts as zero.
        ledger.Record(new LedgerEntry(@"x:\tv\a.mkv", 1000, 350, 28, DateTimeOffset.UnixEpoch));
        ledger.Record(new LedgerEntry(@"X:\TV\C.mkv", 100, 150, 28, DateTimeOffset.UnixEpoch));
        Assert.Equal(850, ledger.TotalSaved());
    }

    [Fact]
    public void Ledger_total_saved_survives_a_reload()
    {
        var file = Path.Combine(Path.GetTempPath(), "shrinkray-test-" + Guid.NewGuid().ToString("N"), "ledger.jsonl");
        try
        {
            var ledger = new ShrinkLedger(file);
            ledger.Record(new LedgerEntry(@"X:\TV\A.mkv", 1000, 400, 28, DateTimeOffset.UnixEpoch));
            ledger.Record(new LedgerEntry(@"X:\TV\B.mkv", 500, 300, 28, DateTimeOffset.UnixEpoch));

            Assert.Equal(800, new ShrinkLedger(file).TotalSaved());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
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
