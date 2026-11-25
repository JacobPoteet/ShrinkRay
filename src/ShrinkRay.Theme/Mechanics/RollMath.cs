using System.Globalization;

namespace ShrinkRay.Theme.Mechanics;

/// <summary>
/// Asks a readout to count up to what it shows now, starting at <paramref name="From"/>.
/// <paramref name="Serial"/> tells one request from the last when both start at the same number.
/// </summary>
public sealed record CounterRoll(string From, int Serial);

/// <summary>The arithmetic of a counter ticking up to its new reading.</summary>
public static class RollMath
{
    /// <summary>How many clicks a roll takes. The steps keep it mechanical rather than smooth.</summary>
    public const int Steps = 24;

    /// <summary>
    /// What the counter reads part way from <paramref name="from"/> to <paramref name="to"/>, in the
    /// decimals and width of <paramref name="to"/>. Anything that is not a climb between two numbers
    /// reads <paramref name="to"/> straight away.
    /// </summary>
    public static string Frame(string? from, string to, double progress)
    {
        ArgumentNullException.ThrowIfNull(to);
        if (progress >= 1 || !TryRead(from, out var start) || !TryRead(to, out var end) || end <= start)
        {
            return to;
        }

        var click = System.Math.Floor(System.Math.Max(0, progress) * Steps) / Steps;
        var point = to.IndexOf('.', StringComparison.Ordinal);
        var decimals = point < 0 ? 0 : to.Length - point - 1;
        var value = start + ((end - start) * click);
        return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture).PadLeft(to.Length);
    }

    private static bool TryRead(string? text, out double value) =>
        double.TryParse(text, NumberStyles.AllowLeadingWhite | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
}
