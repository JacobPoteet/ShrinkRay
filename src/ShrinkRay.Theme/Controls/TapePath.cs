using System.Windows;
using System.Windows.Media;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// The tape run from the source pack, past the guides and the head, to the output pack. Its
/// ends follow the pack radii, in <see cref="TapeDeckMath"/>'s 640 by 256 design space.
/// </summary>
public sealed class TapePath : FrameworkElement
{
    public static readonly DependencyProperty SourcePackProperty = DependencyProperty.Register(
        nameof(SourcePack), typeof(double), typeof(TapePath), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OutputPackProperty = DependencyProperty.Register(
        nameof(OutputPack), typeof(double), typeof(TapePath), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Pen Tape = FrozenPen(Color.FromRgb(0x4A, 0x33, 0x22), 3);

    public double SourcePack
    {
        get => (double)GetValue(SourcePackProperty);
        set => SetValue(SourcePackProperty, value);
    }

    public double OutputPack
    {
        get => (double)GetValue(OutputPackProperty);
        set => SetValue(OutputPackProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var p = TapeDeckMath.TapeRun(SourcePack, OutputPack).Select(q => new Point(q.X, q.Y)).ToArray();
        var figure = new PathFigure { StartPoint = p[0] };
        figure.Segments.Add(new PolyLineSegment(p[1..], true));
        dc.DrawGeometry(null, Tape, new PathGeometry([figure]));
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, thickness) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }
}
