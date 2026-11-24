using ShrinkRay.Theme.Mechanics;
using static ShrinkRay.Theme.Mechanics.Segments;

namespace ShrinkRay.Theme.Tests;

public class SevenSegmentMathTests
{
    public static TheoryData<char, Segments> DigitMap => new()
    {
        { '0', A | B | C | D | E | F },
        { '1', B | C },
        { '2', A | B | G | E | D },
        { '3', A | B | G | C | D },
        { '4', F | G | B | C },
        { '5', A | F | G | C | D },
        { '6', A | F | G | E | D | C },
        { '7', A | B | C },
        { '8', All },
        { '9', A | B | C | D | F | G },
        { '-', G },
        { ' ', None },
        { 'E', A | D | E | F | G },
    };

    [Theory]
    [MemberData(nameof(DigitMap))]
    public void Characters_light_the_design_segments(char c, Segments expected) =>
        Assert.Equal(expected, SevenSegmentMath.SegmentsFor(c));

    [Fact]
    public void S_reads_like_a_five() =>
        Assert.Equal(SevenSegmentMath.SegmentsFor('5'), SevenSegmentMath.SegmentsFor('S'));

    [Fact]
    public void Lower_case_s_and_e_map_to_capitals()
    {
        Assert.Equal(SevenSegmentMath.SegmentsFor('S'), SevenSegmentMath.SegmentsFor('s'));
        Assert.Equal(SevenSegmentMath.SegmentsFor('E'), SevenSegmentMath.SegmentsFor('e'));
    }

    [Theory]
    [InlineData('X')]
    [InlineData('A')]
    [InlineData('?')]
    public void Unsupported_characters_light_nothing(char c)
    {
        Assert.Equal(None, SevenSegmentMath.SegmentsFor(c));
        Assert.False(SevenSegmentMath.IsSupported(c));
    }

    [Fact]
    public void Every_supported_character_is_supported() =>
        Assert.All(SevenSegmentMath.Supported, c => Assert.True(SevenSegmentMath.IsSupported(c)));

    [Fact]
    public void Layout_keeps_the_decimal_point_as_its_own_narrow_cell()
    {
        var cells = SevenSegmentMath.Layout("041.3");

        Assert.Equal(
            [CellKind.Digit, CellKind.Digit, CellKind.Digit, CellKind.Dot, CellKind.Digit],
            cells.Select(c => c.Kind));
        Assert.Equal(SevenSegmentMath.SegmentsFor('4'), cells[1].Lit);
        Assert.Equal(None, cells[3].Lit);
    }

    [Fact]
    public void Layout_reads_a_colon_and_an_episode_code()
    {
        Assert.Equal(CellKind.Colon, SevenSegmentMath.Layout("14:16")[2].Kind);

        var code = SevenSegmentMath.Layout("S01E03");
        Assert.Equal(6, code.Count);
        Assert.All(code, c => Assert.Equal(CellKind.Digit, c.Kind));
        Assert.Equal(SevenSegmentMath.SegmentsFor('E'), code[3].Lit);
    }

    [Fact]
    public void Layout_of_nothing_is_empty()
    {
        Assert.Empty(SevenSegmentMath.Layout(null));
        Assert.Empty(SevenSegmentMath.Layout(string.Empty));
    }

    [Fact]
    public void Blank_positions_stay_as_unlit_digits_so_ghost_segments_show()
    {
        var cells = SevenSegmentMath.Layout("  0.8");
        Assert.Equal(CellKind.Digit, cells[0].Kind);
        Assert.Equal(None, cells[0].Lit);
    }

    [Fact]
    public void Every_segment_polygon_sits_inside_the_digit_box()
    {
        foreach (var segment in SevenSegmentMath.EachSegment)
        {
            Assert.All(SevenSegmentMath.Polygon(segment), p =>
            {
                Assert.InRange(p.X, 0, SevenSegmentMath.DigitBoxWidth);
                Assert.InRange(p.Y, 0, SevenSegmentMath.DigitBoxHeight);
            });
        }
    }

    [Fact]
    public void The_middle_bar_is_a_hexagon_and_the_rest_are_trapezoids()
    {
        Assert.Equal(6, SevenSegmentMath.Polygon(G).Count);
        Assert.All(SevenSegmentMath.EachSegment.Where(s => s != G),
            s => Assert.Equal(4, SevenSegmentMath.Polygon(s).Count));
    }

    [Theory]
    [InlineData(None)]
    [InlineData(A | B)]
    public void Polygon_needs_exactly_one_segment(Segments segment) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SevenSegmentMath.Polygon(segment));
}
