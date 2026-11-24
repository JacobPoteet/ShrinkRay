using System.Globalization;

namespace ShrinkRay.App.ViewModels;

/// <summary>Number formats the deck prints. Sizes carry no unit; labels and headers say GB.</summary>
public static class Format
{
    public const double BytesPerGigabyte = 1_000_000_000d;

    public static double ToGigabytes(long bytes) => bytes / BytesPerGigabyte;

    /// <summary><c>1.62</c></summary>
    public static string Gigabytes(long bytes) => ToGigabytes(bytes).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary><c>292</c>, or one decimal under 10 GB (<c>4.2</c>) so a small show never reads 0.</summary>
    public static string WholeGigabytes(long bytes)
    {
        var gb = ToGigabytes(bytes);
        return gb < 9.95
            ? gb.ToString("0.0", CultureInfo.InvariantCulture)
            : Math.Round(gb).ToString("0", CultureInfo.InvariantCulture);
    }

    /// <summary><c>55%</c> for 0.55.</summary>
    public static string Percent(double fraction) =>
        Math.Round(Math.Clamp(fraction, -9.99, 9.99) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary><c>4H 32M</c>, or <c>12M</c> under an hour.</summary>
    public static string Duration(TimeSpan time)
    {
        var minutes = (int)Math.Ceiling(time.TotalMinutes);
        return minutes >= 60
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes / 60}H {minutes % 60:00}M")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}M");
    }

    /// <summary>Tape time as <c>m:ss</c>, or <c>h:mm</c> from 100 minutes so it fits five digits.</summary>
    public static string TapeTime(double seconds)
    {
        var s = (int)Math.Max(0, Math.Round(seconds));
        return s >= 6000
            ? string.Create(CultureInfo.InvariantCulture, $"{s / 3600}:{s / 60 % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{s / 60}:{s % 60:00}");
    }
}
