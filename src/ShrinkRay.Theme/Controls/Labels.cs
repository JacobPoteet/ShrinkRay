using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShrinkRay.Theme.Controls;

/// <summary>
/// The J-card label on a loaded cassette: the show name in marker, side and season count in
/// silkscreen, an optional episode line in marker, and the orange and amber stripe.
/// </summary>
public class CassetteLabel : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(CassetteLabel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SideProperty = DependencyProperty.Register(
        nameof(Side), typeof(string), typeof(CassetteLabel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty EpisodeProperty = DependencyProperty.Register(
        nameof(Episode), typeof(string), typeof(CassetteLabel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty EpisodeTitleProperty = DependencyProperty.Register(
        nameof(EpisodeTitle), typeof(string), typeof(CassetteLabel), new PropertyMetadata(string.Empty));

    static CassetteLabel()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(CassetteLabel), new FrameworkPropertyMetadata(typeof(CassetteLabel)));
        FocusableProperty.OverrideMetadata(typeof(CassetteLabel), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(CassetteLabel), new FrameworkPropertyMetadata(false));
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>For example "Side A · 6 seasons". Printed uppercase.</summary>
    public string Side
    {
        get => (string)GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    /// <summary>Optional second marker line: the episode code, such as "S01E03", which stays put.</summary>
    public string Episode
    {
        get => (string)GetValue(EpisodeProperty);
        set => SetValue(EpisodeProperty, value);
    }

    /// <summary>The title after the code, such as "My Best Friend's Mistake". It scrolls when it is too long.</summary>
    public string EpisodeTitle
    {
        get => (string)GetValue(EpisodeTitleProperty);
        set => SetValue(EpisodeTitleProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TextLikePeer(this, () =>
        string.Join(", ", new[] { Title, Side, string.Join(" · ", new[] { Episode, EpisodeTitle }.Where(s => !string.IsNullOrWhiteSpace(s))) }
            .Where(s => !string.IsNullOrWhiteSpace(s))));
}

/// <summary>Which roll of embossing tape.</summary>
public enum DymoTape
{
    Black,
    Red,
}

/// <summary>An embossed Dymo label. Use red tape only for warnings about deleting.</summary>
public class DymoLabel : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(DymoLabel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TapeProperty = DependencyProperty.Register(
        nameof(Tape), typeof(DymoTape), typeof(DymoLabel), new PropertyMetadata(DymoTape.Black));

    static DymoLabel()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DymoLabel), new FrameworkPropertyMetadata(typeof(DymoLabel)));
        FocusableProperty.OverrideMetadata(typeof(DymoLabel), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(DymoLabel), new FrameworkPropertyMetadata(false));
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public DymoTape Tape
    {
        get => (DymoTape)GetValue(TapeProperty);
        set => SetValue(TapeProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TextLikePeer(this, () => Text);
}

/// <summary>The 1 px offsets that emboss Dymo letters.</summary>
public static class DymoOffsets
{
    public static TranslateTransform Down { get; } = Frozen(1);

    public static TranslateTransform Up { get; } = Frozen(-1);

    private static TranslateTransform Frozen(double y)
    {
        var t = new TranslateTransform(0, y);
        t.Freeze();
        return t;
    }
}

/// <summary>Exposes a read-only control to screen readers as text.</summary>
internal sealed class TextLikePeer(FrameworkElement owner, Func<string> text) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    protected override string GetClassNameCore() => Owner.GetType().Name;

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? text() ?? string.Empty : name;
    }

    protected override bool IsControlElementCore() => false;

    protected override bool IsContentElementCore() => true;
}
