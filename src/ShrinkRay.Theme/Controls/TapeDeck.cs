using System.Windows;
using System.Windows.Controls;

namespace ShrinkRay.Theme.Controls;

/// <summary>How much of the cassette <see cref="TapeDeck"/> prints.</summary>
public enum TapeDeckDensity
{
    /// <summary>Everything, with the codec captions under the reels. The main deck's cassette.</summary>
    Full,

    /// <summary>
    /// For a small cassette: no codec captions or head lamp, and a label with only the show and
    /// the episode code, set larger so they read at about 270 pixels wide.
    /// </summary>
    Compact,
}

/// <summary>
/// The cassette in its tape window: the label, two reels whose packs show how far the episode has
/// run, the tape path past the guides and the encoder head, and the head's lamp. It draws in the
/// 640 by 256 design space of <see cref="Mechanics.TapeDeckMath"/> and scales to fit its box,
/// keeping its shape, so the main deck and the mini cassette share one drawing.
/// </summary>
public class TapeDeck : Control
{
    public static readonly DependencyProperty SourcePackProperty = DependencyProperty.Register(
        nameof(SourcePack), typeof(double), typeof(TapeDeck), new PropertyMetadata(1.0));

    public static readonly DependencyProperty OutputPackProperty = DependencyProperty.Register(
        nameof(OutputPack), typeof(double), typeof(TapeDeck), new PropertyMetadata(0.0));

    public static readonly DependencyProperty IsSpinningProperty = DependencyProperty.Register(
        nameof(IsSpinning), typeof(bool), typeof(TapeDeck), new PropertyMetadata(false));

    public static readonly DependencyProperty SourceSecondsPerTurnProperty = DependencyProperty.Register(
        nameof(SourceSecondsPerTurn), typeof(double), typeof(TapeDeck),
        new PropertyMetadata(Mechanics.TapeDeckMath.SourceSecondsPerTurn));

    public static readonly DependencyProperty OutputSecondsPerTurnProperty = DependencyProperty.Register(
        nameof(OutputSecondsPerTurn), typeof(double), typeof(TapeDeck),
        new PropertyMetadata(Mechanics.TapeDeckMath.OutputSecondsPerTurn));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(TapeDeck), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty EpisodeProperty = DependencyProperty.Register(
        nameof(Episode), typeof(string), typeof(TapeDeck), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty EpisodeTitleProperty = DependencyProperty.Register(
        nameof(EpisodeTitle), typeof(string), typeof(TapeDeck), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HeadLampProperty = DependencyProperty.Register(
        nameof(HeadLamp), typeof(LampState), typeof(TapeDeck), new PropertyMetadata(LampState.Off));

    public static readonly DependencyProperty SourceCaptionProperty = DependencyProperty.Register(
        nameof(SourceCaption), typeof(string), typeof(TapeDeck), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SourceDetailProperty = DependencyProperty.Register(
        nameof(SourceDetail), typeof(object), typeof(TapeDeck), new PropertyMetadata(null));

    public static readonly DependencyProperty OutputCaptionProperty = DependencyProperty.Register(
        nameof(OutputCaption), typeof(string), typeof(TapeDeck), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty OutputDetailProperty = DependencyProperty.Register(
        nameof(OutputDetail), typeof(object), typeof(TapeDeck), new PropertyMetadata(null));

    public static readonly DependencyProperty DensityProperty = DependencyProperty.Register(
        nameof(Density), typeof(TapeDeckDensity), typeof(TapeDeck), new PropertyMetadata(TapeDeckDensity.Full));

    static TapeDeck()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TapeDeck), new FrameworkPropertyMetadata(typeof(TapeDeck)));
        FocusableProperty.OverrideMetadata(typeof(TapeDeck), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(TapeDeck), new FrameworkPropertyMetadata(false));
    }

    /// <summary>How full the source reel is, 0 to 1.</summary>
    public double SourcePack
    {
        get => (double)GetValue(SourcePackProperty);
        set => SetValue(SourcePackProperty, value);
    }

    /// <summary>How full the output reel is, 0 to 1.</summary>
    public double OutputPack
    {
        get => (double)GetValue(OutputPackProperty);
        set => SetValue(OutputPackProperty, value);
    }

    public bool IsSpinning
    {
        get => (bool)GetValue(IsSpinningProperty);
        set => SetValue(IsSpinningProperty, value);
    }

    public double SourceSecondsPerTurn
    {
        get => (double)GetValue(SourceSecondsPerTurnProperty);
        set => SetValue(SourceSecondsPerTurnProperty, value);
    }

    public double OutputSecondsPerTurn
    {
        get => (double)GetValue(OutputSecondsPerTurnProperty);
        set => SetValue(OutputSecondsPerTurnProperty, value);
    }

    /// <summary>The show's name, printed on the label.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The episode code, such as "S01E03".</summary>
    public string Episode
    {
        get => (string)GetValue(EpisodeProperty);
        set => SetValue(EpisodeProperty, value);
    }

    public string EpisodeTitle
    {
        get => (string)GetValue(EpisodeTitleProperty);
        set => SetValue(EpisodeTitleProperty, value);
    }

    /// <summary>The encoder head's lamp, lit while NVENC works.</summary>
    public LampState HeadLamp
    {
        get => (LampState)GetValue(HeadLampProperty);
        set => SetValue(HeadLampProperty, value);
    }

    /// <summary>The source codec, printed under the source reel.</summary>
    public string SourceCaption
    {
        get => (string)GetValue(SourceCaptionProperty);
        set => SetValue(SourceCaptionProperty, value);
    }

    /// <summary>The source caption's tooltip.</summary>
    public object? SourceDetail
    {
        get => GetValue(SourceDetailProperty);
        set => SetValue(SourceDetailProperty, value);
    }

    /// <summary>The output codec, lit in phosphor under the output reel.</summary>
    public string OutputCaption
    {
        get => (string)GetValue(OutputCaptionProperty);
        set => SetValue(OutputCaptionProperty, value);
    }

    /// <summary>The output caption's tooltip.</summary>
    public object? OutputDetail
    {
        get => GetValue(OutputDetailProperty);
        set => SetValue(OutputDetailProperty, value);
    }

    public TapeDeckDensity Density
    {
        get => (TapeDeckDensity)GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }
}
