using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// One line of text with letter spacing, which TextBlock lacks. Silkscreen labels, the
/// Michroma wordmark and Dymo tape all track wide. <see cref="Tracking"/> is in em, like CSS
/// <c>letter-spacing: .18em</c>.
/// </summary>
public class TrackedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TrackedText),
        new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(
        nameof(Tracking), typeof(double), typeof(TrackedText),
        new FrameworkPropertyMetadata(0.18,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UppercaseProperty = DependencyProperty.Register(
        nameof(Uppercase), typeof(bool), typeof(TrackedText),
        new FrameworkPropertyMetadata(true,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(TrackedText), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(TrackedText), new FrameworkPropertyMetadata(SystemFonts.MessageFontSize,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(
        typeof(TrackedText), new FrameworkPropertyMetadata(FontWeights.Normal,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontStyleProperty = TextElement.FontStyleProperty.AddOwner(
        typeof(TrackedText), new FrameworkPropertyMetadata(FontStyles.Normal,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(TrackedText), new FrameworkPropertyMetadata(Brushes.Black,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private GlyphRun? _run;
    private double _baseline;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Extra space after each character, in em.</summary>
    public double Tracking
    {
        get => (double)GetValue(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    /// <summary>Silkscreen is uppercase, so this defaults to true.</summary>
    public bool Uppercase
    {
        get => (bool)GetValue(UppercaseProperty);
        set => SetValue(UppercaseProperty, value);
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

    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The text as drawn, after the uppercase rule.</summary>
    public string DisplayText => Uppercase ? (Text ?? string.Empty).ToUpperInvariant() : Text ?? string.Empty;

    protected override Size MeasureOverride(Size availableSize)
    {
        _run = null;
        var text = DisplayText;
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal);
        if (!typeface.TryGetGlyphTypeface(out var glyphs))
        {
            return new Size(0, FontSize);
        }

        var size = FontSize;
        var lineHeight = FontFamily.LineSpacing * size;
        _baseline = Math.Round(FontFamily.Baseline * size);
        if (text.Length == 0)
        {
            return new Size(0, lineHeight);
        }

        var pixelsPerDip = (float)VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var round = TextOptions.GetTextFormattingMode(this) == TextFormattingMode.Display;
        var indices = new ushort[text.Length];
        var advances = new double[text.Length];
        var width = 0.0;
        for (var i = 0; i < text.Length; i++)
        {
            if (!glyphs.CharacterToGlyphMap.TryGetValue(text[i], out var index)
                && !glyphs.CharacterToGlyphMap.TryGetValue('?', out index))
            {
                index = 0;
            }

            indices[i] = index;
            var advance = glyphs.AdvanceWidths[index] * size;
            if (i < text.Length - 1)
            {
                advance += Tracking * size;
            }

            if (round)
            {
                advance = Math.Round(advance * pixelsPerDip) / pixelsPerDip;
            }

            advances[i] = advance;
            width += advance;
        }

        _run = new GlyphRun(glyphs, 0, false, size, pixelsPerDip, indices, new Point(0, _baseline), advances,
            null, text.ToCharArray(), null, null, null, XmlLanguage.GetLanguage(CultureInfo.InvariantCulture.IetfLanguageTag));
        return new Size(Math.Ceiling(width), Math.Ceiling(lineHeight));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_run is not null)
        {
            drawingContext.DrawGlyphRun(Foreground, _run);
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        InvalidateMeasure();
        base.OnDpiChanged(oldDpi, newDpi);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TrackedTextAutomationPeer(this);

    private sealed class TrackedTextAutomationPeer(TrackedText owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(TrackedText);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrEmpty(name) ? ((TrackedText)Owner).Text ?? string.Empty : name;
        }

        protected override bool IsControlElementCore() => false;
    }
}
