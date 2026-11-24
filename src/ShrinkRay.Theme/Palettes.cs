using System.Windows.Media;

namespace ShrinkRay.Theme;

/// <summary>The CRT phosphor. It colors every screen, digit and live readout.</summary>
public enum Phosphor
{
    Amber,
    Green,
    Ice,
}

/// <summary>The plastic the unit is molded in.</summary>
public enum Chassis
{
    Beige,
    Graphite,
}

/// <summary>Lit color, unlit (ghost) color and glow for one phosphor.</summary>
public sealed record PhosphorPalette(Color On, Color Dim, Color Glow)
{
    public static PhosphorPalette Amber { get; } =
        new(Rgb(0xFF, 0xB0, 0x00), Rgb(0x3A, 0x28, 0x06), Color.FromArgb(0x8C, 0xFF, 0xB0, 0x00));

    public static PhosphorPalette Green { get; } =
        new(Rgb(0x4D, 0xFF, 0x8F), Rgb(0x0C, 0x3A, 0x1F), Color.FromArgb(0x80, 0x4D, 0xFF, 0x8F));

    public static PhosphorPalette Ice { get; } =
        new(Rgb(0x86, 0xE4, 0xFF), Rgb(0x0E, 0x34, 0x42), Color.FromArgb(0x80, 0x86, 0xE4, 0xFF));

    public static PhosphorPalette For(Phosphor phosphor) => phosphor switch
    {
        Phosphor.Amber => Amber,
        Phosphor.Green => Green,
        Phosphor.Ice => Ice,
        _ => throw new ArgumentOutOfRangeException(nameof(phosphor), phosphor, null),
    };

    internal static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}

/// <summary>
/// The ABS tones of a chassis and the inks printed straight onto it: silkscreen, captions, line
/// printer totals, marker, and the mark on a selected detent. Inks on paper (J-card, VU face)
/// do not change with the chassis.
/// </summary>
public sealed record ChassisPalette(
    Color Abs, Color AbsHi, Color AbsLo, Color Ink, Color InkSoft, Color LinePrint, Color MarkerInk, Color SelectInk)
{
    public static ChassisPalette Beige { get; } = new(
        PhosphorPalette.Rgb(0xD8, 0xCF, 0xBB),
        PhosphorPalette.Rgb(0xE9, 0xE2, 0xD1),
        PhosphorPalette.Rgb(0xB4, 0xA9, 0x8F),
        PhosphorPalette.Rgb(0x3B, 0x35, 0x2A),
        PhosphorPalette.Rgb(0x5B, 0x53, 0x44),
        PhosphorPalette.Rgb(0x2B, 0x2F, 0x3A),
        PhosphorPalette.Rgb(0x1F, 0x3B, 0x73),
        PhosphorPalette.Rgb(0xA3, 0x30, 0x1A));

    // Dark-on-dark inks fail on graphite, so the printed inks lift until they clear WCAG contrast on the card.
    public static ChassisPalette Graphite { get; } = new(
        PhosphorPalette.Rgb(0x4B, 0x48, 0x43),
        PhosphorPalette.Rgb(0x5E, 0x5A, 0x54),
        PhosphorPalette.Rgb(0x33, 0x30, 0x2C),
        PhosphorPalette.Rgb(0xE4, 0xDD, 0xCC),
        PhosphorPalette.Rgb(0xD8, 0xD2, 0xC4),
        PhosphorPalette.Rgb(0xD9, 0xDD, 0xE6),
        PhosphorPalette.Rgb(0xA9, 0xBF, 0xF0),
        PhosphorPalette.Rgb(0xFF, 0xC8, 0xB6));

    public static ChassisPalette For(Chassis chassis) => chassis switch
    {
        Chassis.Beige => Beige,
        Chassis.Graphite => Graphite,
        _ => throw new ArgumentOutOfRangeException(nameof(chassis), chassis, null),
    };
}
