using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShrinkRay.App.Controls;

/// <summary>
/// Holds the deck at its design size or larger. In a smaller space it scales the deck down
/// evenly and lays it out on the space divided by the scale, so the deck fills the window and
/// the longer side gets the spare room. At the design size or larger it passes the space
/// straight through, so the deck looks exactly as designed.
/// </summary>
public sealed class DeckScaler : Decorator
{
    public static readonly DependencyProperty DesignWidthProperty = DependencyProperty.Register(
        nameof(DesignWidth), typeof(double), typeof(DeckScaler),
        new FrameworkPropertyMetadata(1440.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty DesignHeightProperty = DependencyProperty.Register(
        nameof(DesignHeight), typeof(double), typeof(DeckScaler),
        new FrameworkPropertyMetadata(900.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private double _scale = 1;

    /// <summary>The smallest size the deck lays out at, in device-independent pixels.</summary>
    public double DesignWidth
    {
        get => (double)GetValue(DesignWidthProperty);
        set => SetValue(DesignWidthProperty, value);
    }

    public double DesignHeight
    {
        get => (double)GetValue(DesignHeightProperty);
        set => SetValue(DesignHeightProperty, value);
    }

    /// <summary>The scale applied at the last arrange: 1 at the design size or larger.</summary>
    public double Scale => _scale;

    /// <summary>Raised when <see cref="Scale"/> changes, so the window can scale its caption area to match.</summary>
    public event EventHandler? ScaleChanged;

    /// <summary>The even scale that fits a design-sized deck into <paramref name="available"/>, never above 1.</summary>
    public static double ScaleFor(Size available, Size design)
    {
        var x = Fit(available.Width, design.Width);
        var y = Fit(available.Height, design.Height);
        return Math.Min(1, Math.Min(x, y));
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is not { } child)
        {
            return default;
        }

        var scale = ScaleFor(constraint, Design);
        child.Measure(new Size(constraint.Width / scale, constraint.Height / scale));
        return new Size(
            Math.Min(child.DesiredSize.Width * scale, constraint.Width),
            Math.Min(child.DesiredSize.Height * scale, constraint.Height));
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var scale = ScaleFor(arrangeSize, Design);
        if (Child is { } child)
        {
            child.RenderTransform = scale < 1 ? new ScaleTransform(scale, scale) : Transform.Identity;
            child.Arrange(new Rect(0, 0, arrangeSize.Width / scale, arrangeSize.Height / scale));
        }

        if (scale != _scale)
        {
            _scale = scale;
            ScaleChanged?.Invoke(this, EventArgs.Empty);
        }

        return arrangeSize;
    }

    private Size Design => new(DesignWidth, DesignHeight);

    private static double Fit(double available, double design) =>
        double.IsFinite(available) && available > 0 && design > 0 ? available / design : 1;
}
