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
/// The printed parts set into a chassis: the J-card with its status stripes, the meter faces, the
/// cassette label, and the small moulded parts on the tape run. Beige carries cream paper with dark
/// ink. Graphite carries a charcoal card and meter faces with light ink, so no large sheet glares on
/// the dark unit; its cassette label stays cream, a shade down, because ink on dark stock read badly.
/// </summary>
public sealed record PaperPalette(
    Color Card, Color CardRule, Color CardInk, Color CardInkSoft, Color CardInkFaint, Color CardDoneInk, Color CardFailInk,
    Color StripeQueued, Color StripeDone, Color StripeKept, Color StripeSkipped, Color StripeFailed,
    Color Meter, Color MeterInk, Color MeterRed, Color Needle, Color Pivot,
    Color Label, Color LabelInk, Color LabelInkSoft,
    Color Part, Color PartInk, Color Roller)
{
    public static PaperPalette Cream { get; } = new(
        Card: PhosphorPalette.Rgb(0xEF, 0xE7, 0xD4),
        CardRule: PhosphorPalette.Rgb(0xD8, 0xCE, 0xB8),
        CardInk: PhosphorPalette.Rgb(0x2F, 0x2A, 0x21),
        CardInkSoft: PhosphorPalette.Rgb(0x5B, 0x53, 0x44),
        CardInkFaint: PhosphorPalette.Rgb(0x7A, 0x71, 0x60),
        CardDoneInk: PhosphorPalette.Rgb(0x4D, 0x6B, 0x1E),
        CardFailInk: PhosphorPalette.Rgb(0x2A, 0x28, 0x24),
        StripeQueued: PhosphorPalette.Rgb(0xB4, 0xA9, 0x8F),
        StripeDone: PhosphorPalette.Rgb(0x7F, 0xA6, 0x3A),
        StripeKept: PhosphorPalette.Rgb(0x8F, 0x86, 0x70),
        StripeSkipped: PhosphorPalette.Rgb(0x6D, 0x66, 0x5A),
        StripeFailed: PhosphorPalette.Rgb(0x2A, 0x28, 0x24),
        Meter: PhosphorPalette.Rgb(0xF1, 0xE6, 0xC8),
        MeterInk: PhosphorPalette.Rgb(0x3B, 0x35, 0x2A),
        MeterRed: PhosphorPalette.Rgb(0xC0, 0x39, 0x1F),
        Needle: PhosphorPalette.Rgb(0x1B, 0x1B, 0x1B),
        Pivot: PhosphorPalette.Rgb(0x2A, 0x28, 0x24),
        Label: PhosphorPalette.Rgb(0xF1, 0xE9, 0xD6),
        LabelInk: PhosphorPalette.Rgb(0x1F, 0x3B, 0x73),
        LabelInkSoft: PhosphorPalette.Rgb(0x5B, 0x53, 0x44),
        Part: PhosphorPalette.Rgb(0xB4, 0xA9, 0x8F),
        PartInk: PhosphorPalette.Rgb(0x2F, 0x2A, 0x21),
        Roller: PhosphorPalette.Rgb(0xA5, 0x9B, 0x86));

    public static PaperPalette Charcoal { get; } = new(
        Card: PhosphorPalette.Rgb(0x2E, 0x2B, 0x26),
        CardRule: PhosphorPalette.Rgb(0x45, 0x41, 0x3A),
        CardInk: PhosphorPalette.Rgb(0xEC, 0xE4, 0xD2),
        CardInkSoft: PhosphorPalette.Rgb(0xC9, 0xC0, 0xAC),
        CardInkFaint: PhosphorPalette.Rgb(0x9D, 0x94, 0x81),
        CardDoneInk: PhosphorPalette.Rgb(0xA6, 0xD6, 0x51),
        CardFailInk: PhosphorPalette.Rgb(0xFF, 0xFF, 0xFF),
        StripeQueued: PhosphorPalette.Rgb(0x6D, 0x66, 0x5A),
        StripeDone: PhosphorPalette.Rgb(0x7F, 0xA6, 0x3A),
        StripeKept: PhosphorPalette.Rgb(0x9D, 0x94, 0x81),
        StripeSkipped: PhosphorPalette.Rgb(0x55, 0x50, 0x4A),
        StripeFailed: PhosphorPalette.Rgb(0xEC, 0xE4, 0xD2),
        Meter: PhosphorPalette.Rgb(0x2E, 0x2B, 0x26),
        MeterInk: PhosphorPalette.Rgb(0xE4, 0xDD, 0xCC),
        MeterRed: PhosphorPalette.Rgb(0xF0, 0x69, 0x4C),
        Needle: PhosphorPalette.Rgb(0xF1, 0xE9, 0xD6),
        Pivot: PhosphorPalette.Rgb(0x0F, 0x0E, 0x0C),
        Label: PhosphorPalette.Rgb(0xE6, 0xDD, 0xC7),
        LabelInk: PhosphorPalette.Rgb(0x1F, 0x3B, 0x73),
        LabelInkSoft: PhosphorPalette.Rgb(0x4C, 0x45, 0x37),
        Part: PhosphorPalette.Rgb(0x55, 0x50, 0x4A),
        PartInk: PhosphorPalette.Rgb(0xEC, 0xE4, 0xD2),
        Roller: PhosphorPalette.Rgb(0x8F, 0x86, 0x70));
}

/// <summary>
/// The ABS tones of a chassis, the inks printed straight onto it (silkscreen, captions, line
/// printer totals, marker, and the mark on a selected detent) and its <see cref="Paper"/> parts.
/// </summary>
public sealed record ChassisPalette(
    Color Abs, Color AbsHi, Color AbsLo, Color Ink, Color InkSoft, Color LinePrint, Color MarkerInk, Color SelectInk,
    PaperPalette Paper)
{
    public static ChassisPalette Beige { get; } = new(
        PhosphorPalette.Rgb(0xD8, 0xCF, 0xBB),
        PhosphorPalette.Rgb(0xE9, 0xE2, 0xD1),
        PhosphorPalette.Rgb(0xB4, 0xA9, 0x8F),
        PhosphorPalette.Rgb(0x3B, 0x35, 0x2A),
        PhosphorPalette.Rgb(0x5B, 0x53, 0x44),
        PhosphorPalette.Rgb(0x2B, 0x2F, 0x3A),
        PhosphorPalette.Rgb(0x1F, 0x3B, 0x73),
        PhosphorPalette.Rgb(0xA3, 0x30, 0x1A),
        PaperPalette.Cream);

    // Dark-on-dark inks fail on graphite, so the printed inks lift until they clear WCAG contrast on the card.
    public static ChassisPalette Graphite { get; } = new(
        PhosphorPalette.Rgb(0x4B, 0x48, 0x43),
        PhosphorPalette.Rgb(0x5E, 0x5A, 0x54),
        PhosphorPalette.Rgb(0x33, 0x30, 0x2C),
        PhosphorPalette.Rgb(0xE4, 0xDD, 0xCC),
        PhosphorPalette.Rgb(0xD8, 0xD2, 0xC4),
        PhosphorPalette.Rgb(0xD9, 0xDD, 0xE6),
        PhosphorPalette.Rgb(0xA9, 0xBF, 0xF0),
        PhosphorPalette.Rgb(0xFF, 0xC8, 0xB6),
        PaperPalette.Charcoal);

    public static ChassisPalette For(Chassis chassis) => chassis switch
    {
        Chassis.Beige => Beige,
        Chassis.Graphite => Graphite,
        _ => throw new ArgumentOutOfRangeException(nameof(chassis), chassis, null),
    };
}
