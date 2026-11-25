using System.Windows;
using System.Windows.Media;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// The domed acrylic window over a readout, laid on top of its content. It draws the shadow the
/// case throws onto the pane, a glossy cap with a crisp curved edge, and the light caught on the
/// pane's cut edges.
/// </summary>
public class GlassPane : FrameworkElement
{
    private const double Corner = 8;

    private static readonly Brush TopShadow = Frozen(new LinearGradientBrush(Argb(0xA0000000), Argb(0x00000000), 90));
    private static readonly Brush LeftShadow = Frozen(new LinearGradientBrush(Argb(0x66000000), Argb(0x00000000), 0));
    private static readonly Brush RightShadow = Frozen(new LinearGradientBrush(Argb(0x00000000), Argb(0x66000000), 0));
    private static readonly Brush Cap = Frozen(new LinearGradientBrush(Argb(0x26FFFFFF), Argb(0x0AFFFFFF), 90));
    private static readonly Brush TopGlint = Glint((0x00FFFFFF, 0), (0x80FFFFFF, 0.18), (0x4DFFFFFF, 0.55), (0x00FFFFFF, 1));
    private static readonly Brush BottomGlint = Glint((0x00FFFFFF, 0), (0x26FFFFFF, 0.6), (0x40FFFFFF, 0.88), (0x00FFFFFF, 1));
    private static readonly Pen Seat = FrozenPen(Argb(0xE6000000), 2);

    static GlassPane()
    {
        IsHitTestVisibleProperty.OverrideMetadata(typeof(GlassPane), new FrameworkPropertyMetadata(false));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 2 * Corner || h < 2 * Corner)
        {
            return;
        }

        drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), Corner, Corner));
        drawingContext.DrawRectangle(TopShadow, null, new Rect(0, 0, w, 9));
        drawingContext.DrawRectangle(LeftShadow, null, new Rect(0, 0, 8, h));
        drawingContext.DrawRectangle(RightShadow, null, new Rect(w - 8, 0, 8, h));

        var cap = new StreamGeometry();
        using (var path = cap.Open())
        {
            path.BeginFigure(new Point(4, 3), true, true);
            path.LineTo(new Point(w - 4, 3), false, false);
            path.LineTo(new Point(w - 4, h * 0.18), false, false);
            path.QuadraticBezierTo(new Point(w * 0.5, h * 0.48), new Point(4, h * 0.40), false, false);
        }

        cap.Freeze();
        drawingContext.DrawGeometry(Cap, null, cap);
        drawingContext.Pop();

        // The dark seat all round, a catch-light under the top lip and a thinner one along the
        // bottom, where light enters the sheet.
        drawingContext.DrawRoundedRectangle(null, Seat, new Rect(1, 1, w - 2, h - 2), Corner - 1, Corner - 1);
        drawingContext.DrawRectangle(TopGlint, null, new Rect(Corner, 2.5, w - (2 * Corner), 1));
        drawingContext.DrawRectangle(BottomGlint, null, new Rect(Corner, h - 3.5, w - (2 * Corner), 1));
    }

    private static Color Argb(uint value) =>
        Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);

    private static Brush Glint(params (uint Colour, double At)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        foreach (var (colour, at) in stops)
        {
            brush.GradientStops.Add(new GradientStop(Argb(colour), at));
        }

        return Frozen(brush);
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}
