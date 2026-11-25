namespace ShrinkRay.Theme;

/// <summary>
/// Resource keys that change at runtime. Reference them with DynamicResource.
/// <see cref="ThemeManager"/> writes every key listed here.
/// </summary>
public static class ThemeKeys
{
    // Phosphor
    public const string PhosphorColor = "SR.PhosphorColor";
    public const string PhosphorDimColor = "SR.PhosphorDimColor";
    public const string PhosphorGlowColor = "SR.PhosphorGlowColor";
    public const string Phosphor = "SR.Phosphor";
    public const string PhosphorDim = "SR.PhosphorDim";
    public const string PhosphorGlow = "SR.PhosphorGlow";
    public const string PhosphorHalo = "SR.PhosphorHalo";
    public const string PhosphorTextGlow = "SR.PhosphorTextGlow";
    public const string PhosphorStrongGlow = "SR.PhosphorStrongGlow";
    public const string PhosphorBloom = "SR.PhosphorBloom";
    public const string PhosphorRowTint = "SR.PhosphorRowTint";
    public const string PhosphorSpill = "SR.PhosphorSpill";

    // Chassis
    public const string AbsColor = "SR.AbsColor";
    public const string AbsHiColor = "SR.AbsHiColor";
    public const string AbsLoColor = "SR.AbsLoColor";
    public const string InkColor = "SR.InkColor";
    public const string Abs = "SR.Abs";
    public const string AbsHi = "SR.AbsHi";
    public const string AbsLo = "SR.AbsLo";
    public const string Ink = "SR.Ink";
    public const string InkSoft = "SR.InkSoft";
    public const string LinePrinterInk = "SR.LinePrinterInk";
    public const string MarkerInk = "SR.MarkerInk";
    public const string SelectInk = "SR.SelectInk";
    public const string ChassisSurface = "SR.ChassisSurface";
    public const string KeyCap = "SR.KeyCap";
    public const string BezelFill = "SR.BezelFill";

    /// <summary>Every key the manager writes, in a stable order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        PhosphorColor, PhosphorDimColor, PhosphorGlowColor, Phosphor, PhosphorDim, PhosphorGlow,
        PhosphorHalo, PhosphorTextGlow, PhosphorStrongGlow, PhosphorBloom, PhosphorRowTint, PhosphorSpill,
        AbsColor, AbsHiColor, AbsLoColor, InkColor, Abs, AbsHi, AbsLo, Ink, InkSoft,
        LinePrinterInk, MarkerInk, SelectInk, ChassisSurface, KeyCap, BezelFill,
    ];
}
