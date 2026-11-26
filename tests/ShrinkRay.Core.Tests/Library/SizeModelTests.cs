using ShrinkRay.Core.Library;

namespace ShrinkRay.Core.Tests.Library;

public class SizeModelTests
{
    private const string Root = @"X:\TV";
    private const long Original = 1_000_000_000;

    private static LedgerEntry Replaced(string show, int episode, double kept, int cq = 28) => new(
        $@"{Root}\{show}\Season 01\{show} - S01E{episode:00}.mkv", Original, (long)(Original * kept), cq, DateTimeOffset.UnixEpoch);

    private static IEnumerable<LedgerEntry> Show(string show, double kept, int episodes, int cq = 28) =>
        Enumerable.Range(1, episodes).Select(i => Replaced(show, i, kept, cq));

    [Fact]
    public void With_no_history_the_measured_curve_stands()
    {
        Assert.Equal(0.36, SizeModel.Empty.RatioFor("Scrubs", 28), 9);
        Assert.Equal(0.505, SizeModel.From([], Root).RatioFor(null, 25), 9);
    }

    [Fact]
    public void A_show_is_estimated_from_its_own_episodes()
    {
        var model = SizeModel.From([.. Show("Anime", 0.61, 3), .. Show("Sitcom", 0.24, 3)], Root);

        Assert.Equal(0.61, model.RatioFor("anime", 28), 6);
        Assert.Equal(0.24, model.RatioFor("Sitcom", 28), 6);
        Assert.Equal(3, model.EpisodesBehind("Sitcom"));
        Assert.Equal(0, model.EpisodesBehind("New Show"));
    }

    [Fact]
    public void A_result_at_one_quality_moves_the_estimate_at_another()
    {
        var model = SizeModel.From(Show("Sitcom", 0.18, 2), Root);

        // Half the measured curve at CQ 28, so half of it at CQ 25 too.
        Assert.Equal(0.505 / 2, model.RatioFor("Sitcom", 25), 6);
    }

    [Fact]
    public void A_show_without_history_takes_the_librarys_once_there_is_enough_of_it()
    {
        var few = SizeModel.From(Show("Sitcom", 0.54, SizeModel.LibrarySample - 1), Root);
        var enough = SizeModel.From(Show("Sitcom", 0.54, SizeModel.LibrarySample), Root);

        Assert.Equal(0.36, few.RatioFor("New Show", 28), 6);
        Assert.Equal(0.54, enough.RatioFor("New Show", 28), 6);
    }

    [Fact]
    public void Bigger_episodes_count_for_more()
    {
        var small = new LedgerEntry($@"{Root}\Show\a.mkv", 100, 60, 28, DateTimeOffset.UnixEpoch);
        var big = new LedgerEntry($@"{Root}\Show\b.mkv", 900, 180, 28, DateTimeOffset.UnixEpoch);

        Assert.Equal(0.24, SizeModel.From([small, big], Root).RatioFor("Show", 28), 6);
    }

    [Fact]
    public void Odd_entries_are_ignored_and_estimates_stay_in_range()
    {
        var empty = new LedgerEntry($@"{Root}\Show\a.mkv", 0, 0, 28, DateTimeOffset.UnixEpoch);
        var grew = new LedgerEntry($@"{Root}\Show\b.mkv", 100, 400, 28, DateTimeOffset.UnixEpoch);
        var tiny = new LedgerEntry($@"{Root}\Tiny\c.mkv", 1000, 1, 28, DateTimeOffset.UnixEpoch);
        var model = SizeModel.From([empty, grew, tiny], Root);

        Assert.Equal(0.36, model.RatioFor("Show", 28), 6);
        Assert.Equal(0.1, model.RatioFor("Tiny", 28), 6);
        Assert.Equal(0.95, SizeModel.From(Show("Big", 0.9, 1), Root).RatioFor("Big", 20), 6);
    }
}
