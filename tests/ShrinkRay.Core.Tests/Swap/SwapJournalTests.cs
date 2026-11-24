using ShrinkRay.Core.Swap;
using ShrinkRay.Core.Tests.Support;

namespace ShrinkRay.Core.Tests.Swap;

public class SwapJournalTests
{
    private const string File1 = @"X:\TV\Example Show\Season 01\Example Show S01E01.mkv";

    [Theory]
    [InlineData(null, SwapState.Queued, true)]
    [InlineData(null, SwapState.Verified, false)]
    [InlineData(SwapState.Queued, SwapState.CopiedToScratch, true)]
    [InlineData(SwapState.Queued, SwapState.Skipped, true)]
    [InlineData(SwapState.Encoded, SwapState.Verified, true)]
    [InlineData(SwapState.Encoded, SwapState.FontsRestored, true)]
    [InlineData(SwapState.Encoded, SwapState.UploadedTemp, false)]
    [InlineData(SwapState.Verified, SwapState.UploadedTemp, true)]
    [InlineData(SwapState.Verified, SwapState.KeptOriginal, true)]
    [InlineData(SwapState.Verified, SwapState.Swapped, false)]
    [InlineData(SwapState.UploadedTemp, SwapState.Swapped, false)]
    [InlineData(SwapState.Swapped, SwapState.OriginalDeleted, true)]
    [InlineData(SwapState.Swapped, SwapState.Failed, true)]
    [InlineData(SwapState.OriginalDeleted, SwapState.OriginalDeleted, true)]
    [InlineData(SwapState.Failed, SwapState.Queued, true)]
    [InlineData(SwapState.Failed, SwapState.Verified, false)]
    [InlineData(SwapState.KeptOriginal, SwapState.UploadedTemp, false)]
    [InlineData(SwapState.Skipped, SwapState.Queued, true)]
    [InlineData(SwapState.Queued, SwapState.Cancelled, true)]
    [InlineData(SwapState.Encoded, SwapState.Cancelled, true)]
    [InlineData(SwapState.Verified, SwapState.Cancelled, true)]
    [InlineData(SwapState.UploadedTemp, SwapState.Cancelled, true)]
    [InlineData(SwapState.OriginalRenamedAside, SwapState.Cancelled, false)]
    [InlineData(SwapState.Swapped, SwapState.Cancelled, false)]
    [InlineData(SwapState.OriginalDeleted, SwapState.Cancelled, false)]
    [InlineData(SwapState.Cancelled, SwapState.Queued, true)]
    [InlineData(SwapState.Cancelled, SwapState.CopiedToScratch, false)]
    public void Transitions_follow_the_state_machine(SwapState? from, SwapState to, bool allowed)
    {
        Assert.Equal(allowed, SwapTransitions.IsAllowed(from, to));
    }

    [Fact]
    public void Every_state_has_a_way_out()
    {
        foreach (var state in Enum.GetValues<SwapState>())
        {
            Assert.Contains(Enum.GetValues<SwapState>(), to => SwapTransitions.IsAllowed(state, to));
        }
    }

    [Fact]
    public void Record_rejects_a_skipped_step_and_writes_nothing()
    {
        var store = new InMemoryJournalStore();
        var journal = new SwapJournal(store);
        journal.Record(File1, SwapState.Queued);

        Assert.Throws<InvalidOperationException>(() => journal.Record(File1, SwapState.UploadedTemp));
        Assert.Single(store.Entries);
    }

    [Fact]
    public void Latest_state_survives_a_reload_and_ignores_path_case()
    {
        var store = new InMemoryJournalStore();
        var journal = new SwapJournal(store);
        journal.Record(File1, SwapState.Queued);
        journal.Record(File1, SwapState.CopiedToScratch, "copying");

        var reloaded = new SwapJournal(store);

        Assert.Equal(SwapState.CopiedToScratch, reloaded.CurrentState(File1.ToUpperInvariant()));
        Assert.Equal("copying", reloaded.Latest(File1)!.Reason);
        Assert.Single(reloaded.LatestEntries());
    }

    [Fact]
    public void Journal_store_failure_leaves_the_state_unchanged()
    {
        var crash = new CrashSwitch();
        var journal = new SwapJournal(new InMemoryJournalStore(crash));
        journal.Record(File1, SwapState.Queued);
        crash.CrashAfter(0);

        Assert.Throws<SimulatedCrashException>(() => journal.Record(File1, SwapState.CopiedToScratch));
        Assert.Equal(SwapState.Queued, journal.CurrentState(File1));
    }
}

public sealed class JsonLinesJournalStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ShrinkRayTests", Guid.NewGuid().ToString("N"));

    private string JournalPath => Path.Combine(_folder, "journal.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_reads_as_empty()
    {
        Assert.Empty(new JsonLinesJournalStore(JournalPath).ReadAll());
    }

    [Fact]
    public void Entries_round_trip_one_per_line()
    {
        var store = new JsonLinesJournalStore(JournalPath);
        var time = new DateTimeOffset(2026, 11, 24, 1, 2, 3, TimeSpan.Zero);
        store.Append(new JournalEntry(time, @"X:\TV\Show\a.mkv", SwapState.Queued));
        store.Append(new JournalEntry(time, @"X:\TV\Show\a.mkv", SwapState.Failed, "reason with \"quotes\""));

        var lines = File.ReadAllLines(JournalPath);
        var entries = new JsonLinesJournalStore(JournalPath).ReadAll();

        Assert.Equal(2, lines.Length);
        Assert.Contains("\"state\":\"Queued\"", lines[0]);
        Assert.Equal(2, entries.Count);
        Assert.Equal(SwapState.Failed, entries[1].State);
        Assert.Equal("reason with \"quotes\"", entries[1].Reason);
        Assert.Equal(time, entries[0].Timestamp);
    }

    [Fact]
    public void Torn_and_corrupt_lines_are_skipped()
    {
        var store = new JsonLinesJournalStore(JournalPath);
        store.Append(new JournalEntry(DateTimeOffset.UtcNow, @"X:\TV\Show\a.mkv", SwapState.Queued));
        File.AppendAllText(JournalPath, "not json\n{\"timestamp\":\"2026-11-24T00:00:00+00:00\",\"path\":\"X:\\\\TV");

        var entries = store.ReadAll();

        Assert.Single(entries);
    }

    [Fact]
    public void Appends_after_a_torn_line_still_read_back()
    {
        var store = new JsonLinesJournalStore(JournalPath);
        Directory.CreateDirectory(_folder);
        File.WriteAllText(JournalPath, "{\"broken\n");
        store.Append(new JournalEntry(DateTimeOffset.UtcNow, @"X:\TV\Show\a.mkv", SwapState.Queued));

        Assert.Single(store.ReadAll());
    }
}
