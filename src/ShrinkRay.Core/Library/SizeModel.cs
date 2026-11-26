using ShrinkRay.Core.Media;

namespace ShrinkRay.Core.Library;

/// <summary>
/// Size estimates that learn from the ledger. Shows differ widely at the same CQ, so a show with
/// replaced episodes is estimated from its own results, and one without from the library's.
/// Both are kept as a factor on <see cref="SizeEstimate"/>'s measured curve, so a result at one
/// CQ also moves the estimate at another. Immutable: build a new one when the ledger changes.
/// </summary>
public sealed class SizeModel
{
    /// <summary>Replaced episodes the library needs before its own results beat the measured clips.</summary>
    public const int LibrarySample = 5;

    private const double MinRatio = 0.1;
    private const double MaxRatio = 0.95;

    private readonly Dictionary<string, (double Factor, int Episodes)> _shows;
    private readonly double _library;

    private SizeModel(Dictionary<string, (double Factor, int Episodes)> shows, double library)
    {
        _shows = shows;
        _library = library;
    }

    /// <summary>The measured curve alone.</summary>
    public static SizeModel Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase), 1);

    /// <param name="libraryRoot">Names each entry's show, as the scan does.</param>
    public static SizeModel From(IEnumerable<LedgerEntry> entries, string? libraryRoot)
    {
        var usable = entries.Where(e => e.OriginalBytes > 0 && e.NewBytes > 0 && e.NewBytes <= e.OriginalBytes).ToList();

        // Bytes written over the bytes the curve expected, so a big episode counts for more.
        static double Factor(IEnumerable<LedgerEntry> group) =>
            group.Sum(e => (double)e.NewBytes) / group.Sum(e => e.OriginalBytes * SizeEstimate.RatioFor(e.Cq));

        var shows = usable
            .GroupBy(e => ShowFolder.NameFor(e.Path, libraryRoot) ?? "", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0)
            .ToDictionary(g => g.Key, g => (Factor(g), g.Count()), StringComparer.OrdinalIgnoreCase);
        return new SizeModel(shows, usable.Count >= LibrarySample ? Factor(usable) : 1);
    }

    /// <summary>Output size over source size expected for a show's episodes at a CQ.</summary>
    public double RatioFor(string? show, int cq)
    {
        var factor = show is not null && _shows.TryGetValue(show, out var own) ? own.Factor : _library;
        return Math.Clamp(SizeEstimate.RatioFor(cq) * factor, MinRatio, MaxRatio);
    }

    /// <summary>How many of the show's own replaced episodes the estimate rests on.</summary>
    public int EpisodesBehind(string? show) => show is not null && _shows.TryGetValue(show, out var own) ? own.Episodes : 0;
}
