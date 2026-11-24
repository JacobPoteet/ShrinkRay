using System.Windows.Media.Animation;

namespace ShrinkRay.Theme.Mechanics;

/// <summary>
/// CSS <c>cubic-bezier(x1, y1, x2, y2)</c> as a WPF easing function. A y above 1 overshoots, which
/// is how the needles and the knob swing past their mark and settle.
/// </summary>
public sealed class CubicBezierEase : IEasingFunction
{
    /// <summary>The needle ease from the design: cubic-bezier(.3, 1.6, .5, 1).</summary>
    public static CubicBezierEase Needle { get; } = new(0.3, 1.6, 0.5, 1);

    /// <summary>The knob ease from the design: cubic-bezier(.3, 1.5, .5, 1).</summary>
    public static CubicBezierEase Knob { get; } = new(0.3, 1.5, 0.5, 1);

    public CubicBezierEase(double x1, double y1, double x2, double y2)
    {
        if (x1 is < 0 or > 1 || x2 is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(x1), "x1 and x2 must lie in 0..1.");
        }

        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }

    /// <summary>Eased progress for time <paramref name="normalizedTime"/> in 0..1.</summary>
    public double Ease(double normalizedTime)
    {
        if (normalizedTime <= 0)
        {
            return 0;
        }

        if (normalizedTime >= 1)
        {
            return 1;
        }

        return Bezier(SolveT(normalizedTime), Y1, Y2);
    }

    private static double Bezier(double t, double p1, double p2)
    {
        var u = 1 - t;
        return 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t;
    }

    private static double BezierSlope(double t, double p1, double p2)
    {
        var u = 1 - t;
        return 3 * u * u * p1 + 6 * u * t * (p2 - p1) + 3 * t * t * (1 - p2);
    }

    private double SolveT(double x)
    {
        // Newton first; fall back to bisection where the slope flattens.
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var error = Bezier(t, X1, X2) - x;
            if (System.Math.Abs(error) < 1e-7)
            {
                return t;
            }

            var slope = BezierSlope(t, X1, X2);
            if (System.Math.Abs(slope) < 1e-6)
            {
                break;
            }

            t -= error / slope;
        }

        double lo = 0, hi = 1;
        t = x;
        for (var i = 0; i < 40; i++)
        {
            var value = Bezier(t, X1, X2);
            if (System.Math.Abs(value - x) < 1e-7)
            {
                break;
            }

            if (value < x)
            {
                lo = t;
            }
            else
            {
                hi = t;
            }

            t = (lo + hi) / 2;
        }

        return t;
    }
}
