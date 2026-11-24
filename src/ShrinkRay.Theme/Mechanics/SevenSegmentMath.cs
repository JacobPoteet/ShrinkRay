using System.Windows;

namespace ShrinkRay.Theme.Mechanics;

/// <summary>The seven segments, named the usual way: a on top, then clockwise, g in the middle.</summary>
[Flags]
public enum Segments
{
    None = 0,
    A = 1,
    B = 2,
    C = 4,
    D = 8,
    E = 16,
    F = 32,
    G = 64,
    All = A | B | C | D | E | F | G,
}

/// <summary>What one position in a seven-segment readout holds.</summary>
public enum CellKind
{
    Digit,
    Dot,
    Colon,
}

/// <summary>One position in the readout. Digits carry their lit segments.</summary>
public readonly record struct SegmentCell(CellKind Kind, Segments Lit);

/// <summary>
/// Segment map and glyph geometry for <see cref="Controls.SevenSegmentDisplay"/>. Geometry is in
/// the 24 by 40 box the design draws each digit in.
/// </summary>
public static class SevenSegmentMath
{
    public const double DigitBoxWidth = 24;
    public const double DigitBoxHeight = 40;

    /// <summary>Characters the display can show.</summary>
    public const string Supported = "0123456789 -SE:.";

    private static readonly Dictionary<char, Segments> Map = new()
    {
        ['0'] = Segments.A | Segments.B | Segments.C | Segments.D | Segments.E | Segments.F,
        ['1'] = Segments.B | Segments.C,
        ['2'] = Segments.A | Segments.B | Segments.G | Segments.E | Segments.D,
        ['3'] = Segments.A | Segments.B | Segments.G | Segments.C | Segments.D,
        ['4'] = Segments.F | Segments.G | Segments.B | Segments.C,
        ['5'] = Segments.A | Segments.F | Segments.G | Segments.C | Segments.D,
        ['6'] = Segments.A | Segments.F | Segments.G | Segments.E | Segments.D | Segments.C,
        ['7'] = Segments.A | Segments.B | Segments.C,
        ['8'] = Segments.All,
        ['9'] = Segments.A | Segments.B | Segments.C | Segments.D | Segments.F | Segments.G,
        ['-'] = Segments.G,
        [' '] = Segments.None,
        ['S'] = Segments.A | Segments.F | Segments.G | Segments.C | Segments.D,
        ['E'] = Segments.A | Segments.D | Segments.E | Segments.F | Segments.G,
    };

    /// <summary>
    /// The segments lit for <paramref name="c"/>. Lower-case s and e map to their capitals.
    /// Characters the display cannot show light nothing.
    /// </summary>
    public static Segments SegmentsFor(char c) =>
        Map.TryGetValue(char.ToUpperInvariant(c), out var s) ? s : Segments.None;

    /// <summary>True when <paramref name="c"/> is in <see cref="Supported"/>, ignoring case.</summary>
    public static bool IsSupported(char c) => Supported.Contains(char.ToUpperInvariant(c));

    /// <summary>Splits text into digit, dot and colon cells, in order.</summary>
    public static IReadOnlyList<SegmentCell> Layout(string? text)
    {
        var cells = new List<SegmentCell>();
        foreach (var c in text ?? string.Empty)
        {
            cells.Add(c switch
            {
                '.' => new SegmentCell(CellKind.Dot, Segments.None),
                ':' => new SegmentCell(CellKind.Colon, Segments.None),
                _ => new SegmentCell(CellKind.Digit, SegmentsFor(c)),
            });
        }

        return cells;
    }

    /// <summary>The polygon for one segment, in the 24 by 40 digit box.</summary>
    public static IReadOnlyList<Point> Polygon(Segments segment) => segment switch
    {
        Segments.A => [new(5, 1), new(19, 1), new(16, 4), new(8, 4)],
        Segments.B => [new(20, 2), new(20, 19), new(17, 17), new(17, 5)],
        Segments.C => [new(20, 21), new(20, 38), new(17, 35), new(17, 23)],
        Segments.D => [new(5, 39), new(19, 39), new(16, 36), new(8, 36)],
        Segments.E => [new(4, 21), new(4, 38), new(7, 35), new(7, 23)],
        Segments.F => [new(4, 2), new(4, 19), new(7, 17), new(7, 5)],
        Segments.G => [new(6, 20), new(8, 18.5), new(16, 18.5), new(18, 20), new(16, 21.5), new(8, 21.5)],
        _ => throw new ArgumentOutOfRangeException(nameof(segment), segment, "Pass exactly one segment."),
    };

    /// <summary>The seven single segments in drawing order.</summary>
    public static IReadOnlyList<Segments> EachSegment { get; } =
        [Segments.A, Segments.B, Segments.C, Segments.D, Segments.E, Segments.F, Segments.G];
}
