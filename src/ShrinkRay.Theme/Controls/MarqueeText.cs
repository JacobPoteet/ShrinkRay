using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// One line of text that scrolls when it does not fit, like a long track name on a player. It
/// does nothing when the text fits. When it overflows it waits, scrolls slowly until the end
/// shows, waits again, then fades out and back in at the start (see <see cref="MarqueeMath"/>).
/// Under reduced motion it holds still and ends in an ellipsis, with the full text in the
/// tooltip and the automation name.
/// </summary>
/// <remarks>
/// One animation clock drives a phase value. Each frame moves a render transform and, during the
/// fade, the opacity, so no layout runs per frame. It runs only while the text overflows.
/// </remarks>
public class MarqueeText : Decorator
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MarqueeText),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, OnTextChanged));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(MarqueeText));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(MarqueeText));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(MarqueeText));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(MarqueeText));

    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        "Phase", typeof(double), typeof(MarqueeText), new PropertyMetadata(0.0, OnPhaseChanged));

    private double _fadeWidth;
    private Brush? _rightFade;
    private Brush? _bothFade;
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TranslateTransform _shift = new();
    private double _textWidth;
    private double _distance;
    private bool _running;

    public MarqueeText()
    {
        _text.RenderTransform = _shift;
        Child = _text;
        ClipToBounds = true;
        Loaded += (_, _) =>
        {
            MotionPolicy.Changed += OnMotionChanged;
            UpdateMotion();
        };
        Unloaded += (_, _) =>
        {
            MotionPolicy.Changed -= OnMotionChanged;
            Stop();
        };
        IsVisibleChanged += (_, _) => UpdateMotion();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>True while the text is wider than the room it has.</summary>
    public bool IsOverflowing { get; private set; }

    /// <summary>True while the scroll animation runs. False when the text fits or motion is held.</summary>
    public bool IsScrolling => _running;

    /// <summary>The current leftward shift of the text.</summary>
    public double Offset => -_shift.X;

    /// <summary>How far the text travels from start to end, or zero when it fits.</summary>
    public double ScrollDistance => _distance;

    /// <summary>Seconds into the cycle. Tests and render harnesses set it to freeze a frame.</summary>
    public void SeekTo(double seconds)
    {
        Stop();
        Apply(seconds);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _text.Text = Text ?? string.Empty;
        _text.Measure(new Size(double.PositiveInfinity, constraint.Height));
        _textWidth = _text.DesiredSize.Width;
        var width = double.IsInfinity(constraint.Width) ? _textWidth : Math.Min(_textWidth, constraint.Width);

        // Still, a title that does not fit is laid out at the room it has, which trims it with an
        // ellipsis. Scrolling, it keeps its full width and the clip does the cutting.
        var still = !MotionPolicy.IsMotionEnabled && MarqueeMath.Overflows(_textWidth, width);
        _text.TextTrimming = still ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        if (still)
        {
            _text.Measure(new Size(width, constraint.Height));
        }

        return new Size(Math.Ceiling(width), _text.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var overflows = MarqueeMath.Overflows(_textWidth, arrangeSize.Width);
        var scrolls = overflows && MotionPolicy.IsMotionEnabled;

        _text.Arrange(new Rect(0, 0, scrolls ? _textWidth : arrangeSize.Width, arrangeSize.Height));

        var distance = MarqueeMath.Distance(_textWidth, arrangeSize.Width);
        var changed = overflows != IsOverflowing || Math.Abs(distance - _distance) > 0.5;
        IsOverflowing = overflows;
        _distance = distance;
        ToolTip = overflows ? Text : null;
        if (!scrolls)
        {
            _rightFade = _bothFade = null;
            OpacityMask = null;
        }
        else if (_rightFade is null || Math.Abs(_fadeWidth - arrangeSize.Width) > 0.5)
        {
            _fadeWidth = arrangeSize.Width;
            _rightFade = CreateEdgeFade(arrangeSize.Width, fadeLeft: false);
            _bothFade = CreateEdgeFade(arrangeSize.Width, fadeLeft: true);
            UpdateFade();
        }

        if (changed)
        {
            Dispatcher.BeginInvoke(UpdateMotion);
        }

        return arrangeSize;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TextLikePeer(this, () => Text);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var marquee = (MarqueeText)d;

        // A new title starts from the beginning.
        marquee.Stop();
        marquee.Apply(0);
        marquee.Dispatcher.BeginInvoke(marquee.UpdateMotion);
    }

    private static void OnPhaseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MarqueeText)d).Apply((double)e.NewValue);

    /// <summary>
    /// Clear over the last <see cref="MarqueeMath.EdgeFadeWidth"/> px of the viewport, and over the first
    /// <see cref="MarqueeMath.EdgeFadeWidth"/> px too when <paramref name="fadeLeft"/> is set. The left fade is
    /// only used once the text has scrolled, so the title's first letters stay crisp at rest.
    /// </summary>
    private static Brush CreateEdgeFade(double width, bool fadeLeft)
    {
        var edge = MarqueeMath.EdgeStop(width);
        var stops = new GradientStopCollection();
        if (fadeLeft)
        {
            stops.Add(new GradientStop(Colors.Transparent, 0));
            stops.Add(new GradientStop(Colors.Black, edge));
        }
        else
        {
            stops.Add(new GradientStop(Colors.Black, 0));
        }

        stops.Add(new GradientStop(Colors.Black, 1 - edge));
        stops.Add(new GradientStop(Colors.Transparent, 1));
        var brush = new LinearGradientBrush(stops, new Point(0, 0), new Point(Math.Max(1, width), 0))
        {
            MappingMode = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Fades the left edge only while the text is shifted away from its start.</summary>
    private void UpdateFade()
    {
        var mask = MarqueeMath.FadesLeft(-_shift.X) ? _bothFade : _rightFade;
        if (!ReferenceEquals(OpacityMask, mask))
        {
            OpacityMask = mask;
        }
    }

    private void OnMotionChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        InvalidateMeasure();
        UpdateMotion();
    });

    private void UpdateMotion()
    {
        Stop();
        Apply(0);
        if (!IsLoaded || !IsVisible || !IsOverflowing || !MotionPolicy.IsMotionEnabled)
        {
            return;
        }

        var cycle = MarqueeMath.CycleSeconds(_distance);
        if (cycle <= 0)
        {
            return;
        }

        _running = true;
        BeginAnimation(PhaseProperty, new DoubleAnimation(0, cycle, TimeSpan.FromSeconds(cycle))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    private void Stop()
    {
        if (_running)
        {
            BeginAnimation(PhaseProperty, null);
            _running = false;
        }
    }

    private void Apply(double seconds)
    {
        var (offset, opacity) = MarqueeMath.FrameAt(seconds, _distance);
        _shift.X = -offset;
        _text.Opacity = opacity;
        UpdateFade();
    }
}
