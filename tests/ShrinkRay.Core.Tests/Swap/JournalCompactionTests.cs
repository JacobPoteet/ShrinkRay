using ShrinkRay.Core.Swap;
using ShrinkRay.Core.Tests.Support;

namespace ShrinkRay.Core.Tests.Swap;

public class JournalCompactionTests
{
    private const string A = @"X:\TV\Show\a.mkv";
    private const string B = @"X:\TV\Show\b.mkv";
    private const string C = @"X:\TV\Show\c.mkv";

    private static readonly SwapState[] Success =
    [
        SwapState.Queued, SwapState.CopiedToScratch, SwapState.Encoded, SwapState.Verified,
        SwapState.UploadedTemp, SwapState.OriginalRenamedAside, SwapState.Swapped, SwapState.OriginalDeleted,
    ];

    private static void Record(SwapJournal journal, string path, params SwapState[] states)
    {
        foreach (var state in states)
        {
            journal.Record(path, state);
        }
    }

    [Fact]
    public void Keeps_one_line_per_file_in_the_order_of_each_files_last_entry()
    {
        var store = new InMemoryJournalStore();
        var journal = new SwapJournal(store);
        Record(journal, A, SwapState.Queued, SwapState.CopiedToScratch);
        Record(journal, B, SwapState.Queued);
        Record(journal, A, SwapState.Encoded);
        Record(journal, C, SwapState.Queued, SwapState.Skipped);

        var dropped = journal.Compact();

        Assert.Equal(3, dropped);
        Assert.Equal([B, A, C], store.Entries.Select(e => e.Path));
        Assert.Equal([SwapState.Queued, SwapState.Encoded, SwapState.Skipped], store.Entries.Select(e => e.State));
    }

    public static TheoryData<SwapState, SwapState[]> HistoriesEndingIn() => new()
    {
        { SwapState.OriginalDeleted, Success },
        { SwapState.KeptOriginal, [SwapState.Queued, SwapState.CopiedToScratch, SwapState.Encoded, SwapState.Verified, SwapState.KeptOriginal] },
        { SwapState.Skipped, [SwapState.Queued, SwapState.Skipped] },
        { SwapState.Cancelled, [SwapState.Queued, SwapState.CopiedToScratch, SwapState.Cancelled] },
        { SwapState.Failed, [SwapState.Queued, SwapState.CopiedToScratch, SwapState.Failed] },
        { SwapState.CopiedToScratch, [SwapState.Queued, SwapState.CopiedToScratch] },
        { SwapState.UploadedTemp, Success[..5] },
        { SwapState.OriginalRenamedAside, Success[..6] },
        { SwapState.Swapped, Success[..7] },
    };

    [Theory]
    [MemberData(nameof(HistoriesEndingIn))]
    public void The_last_entry_survives_whole_whatever_state_it_is_in(SwapState last, SwapState[] history)
    {
        var store = new InMemoryJournalStore();
        var journal = new SwapJournal(store);
        foreach (var state in history)
        {
            journal.Record(A, state, "why " + state);
        }

        var before = journal.Latest(A)!;
        journal.Compact();
        var reloaded = new SwapJournal(store);

        Assert.Single(store.Entries);
        Assert.Equal(before, reloaded.Latest(A));
        Assert.Equal(last, reloaded.CurrentState(A));
    }

    [Fact]
    public void A_journal_with_one_line_per_file_is_not_rewritten()
    {
        var crash = new CrashSwitch();
        var store = new InMemoryJournalStore(crash);
        var journal = new SwapJournal(store);
        Record(journal, A, SwapState.Queued);
        Record(journal, B, SwapState.Queued);
        crash.CrashAfter(0);

        var dropped = journal.Compact();

        Assert.Equal(0, dropped);
        Assert.Equal(0, crash.Operations);
    }

    [Fact]
    public void Compacting_twice_changes_nothing_the_second_time()
    {
        var store = new InMemoryJournalStore();
        var journal = new SwapJournal(store);
        Record(journal, A, Success);
        Record(journal, A, SwapState.Queued, SwapState.CopiedToScratch);

        Assert.Equal(9, journal.Compact());
        Assert.Equal(0, journal.Compact());
        Assert.Equal(SwapState.CopiedToScratch, new SwapJournal(store).CurrentState(A));
    }

    [Fact]
    public void The_state_machine_still_applies_after_compaction()
    {
        var store = new InMemoryJournalStore();
        var journal = new SwapJournal(store);
        Record(journal, A, Success);
        journal.Compact();
        var reloaded = new SwapJournal(store);

        Assert.Throws<InvalidOperationException>(() => reloaded.Record(A, SwapState.Swapped));
        reloaded.Record(A, SwapState.Queued);

        Assert.Equal(SwapState.Queued, reloaded.CurrentState(A));
    }

    [Fact]
    public void A_store_that_cannot_compact_is_left_alone()
    {
        var store = new AppendOnlyStore();
        var journal = new SwapJournal(store);
        Record(journal, A, SwapState.Queued, SwapState.Skipped);

        Assert.Equal(0, journal.Compact());
        Assert.Equal(2, store.ReadAll().Count);
    }

    private sealed class AppendOnlyStore : IJournalStore
    {
        private readonly List<JournalEntry> _entries = [];

        public void Append(JournalEntry entry) => _entries.Add(entry);

        public IReadOnlyList<JournalEntry> ReadAll() => _entries.ToList();
    }
}

public class JsonLinesJournalCompactionTests : IDisposable
{
    private const string A = @"X:\TV\Show\a.mkv";
    private const string B = @"X:\TV\Show\b.mkv";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "shrinkray-compact-" + Guid.NewGuid().ToString("N"));

    private string JournalPath => Path.Combine(_folder, "journal.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static SwapJournal Fill(JsonLinesJournalStore store)
    {
        var journal = new SwapJournal(store);
        foreach (var state in new[] { SwapState.Queued, SwapState.CopiedToScratch, SwapState.Encoded })
        {
            journal.Record(A, state);
        }

        journal.Record(B, SwapState.Queued);
        journal.Record(B, SwapState.Skipped, "skipped \"by hand\"");
        return journal;
    }

    [Fact]
    public void Rewrites_the_file_with_the_last_line_per_path_and_leaves_no_temp_file()
    {
        var store = new JsonLinesJournalStore(JournalPath);
        var journal = Fill(store);

        Assert.Equal(3, journal.Compact());

        Assert.Equal(2, File.ReadAllLines(JournalPath).Length);
        var reloaded = new SwapJournal(new JsonLinesJournalStore(JournalPath));
        Assert.Equal(SwapState.Encoded, reloaded.CurrentState(A));
        Assert.Equal("skipped \"by hand\"", reloaded.Latest(B)!.Reason);
        Assert.Equal([JournalPath], Directory.GetFiles(_folder));
    }

    [Fact]
    public void Appends_after_a_compaction_land_in_the_new_file()
    {
        var store = new JsonLinesJournalStore(JournalPath);
        var journal = Fill(store);
        journal.Compact();

        journal.Record(A, SwapState.Verified);

        var reloaded = new SwapJournal(new JsonLinesJournalStore(JournalPath));
        Assert.Equal(SwapState.Verified, reloaded.CurrentState(A));
        Assert.Equal(3, File.ReadAllLines(JournalPath).Length);
    }

    [Fact]
    public void A_temp_file_left_by_a_crash_before_the_replace_changes_nothing_and_is_overwritten()
    {
        var store = new JsonLinesJournalStore(JournalPath);
        var journal = Fill(store);
        File.WriteAllText(JournalPath + ".compact.tmp", "half written garbage");

        Assert.Equal(5, store.ReadAll().Count);
        Assert.Equal(3, journal.Compact());

        Assert.Equal(2, File.ReadAllLines(JournalPath).Length);
        Assert.DoesNotContain("garbage", File.ReadAllText(JournalPath));
        Assert.False(File.Exists(JournalPath + ".compact.tmp"));
    }

    [Fact]
    public void ReplaceAll_creates_the_journal_when_none_exists()
    {
        var store = new JsonLinesJournalStore(JournalPath);

        store.ReplaceAll([new JournalEntry(DateTimeOffset.UtcNow, A, SwapState.Queued)]);

        Assert.Single(store.ReadAll());
    }
}
