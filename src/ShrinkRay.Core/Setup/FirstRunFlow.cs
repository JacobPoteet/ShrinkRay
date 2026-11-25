using ShrinkRay.Core.Settings;

namespace ShrinkRay.Core.Setup;

/// <summary>Where first-run setup stands: a card, a stop on the tour, or off.</summary>
public enum FirstRunStep
{
    Off,

    /// <summary>The self-test: every tool must be found.</summary>
    Tools,

    /// <summary>Where the TV library is.</summary>
    Library,

    /// <summary>Where encodes happen.</summary>
    Scratch,

    /// <summary>Whether originals are replaced.</summary>
    Originals,

    /// <summary>The tour's first stop, the library list.</summary>
    TourLibrary,

    /// <summary>The tour's second stop, the Encode key.</summary>
    TourEncode,

    /// <summary>The tour's last stop, the savings bar.</summary>
    TourSavings,
}

/// <summary>What setup knows so far. Each card needs one of these before it moves on.</summary>
/// <param name="ReplaceOriginals">The choice on the originals card. Null until the user picks.</param>
public sealed record FirstRunFacts(bool ToolsFound, bool LibraryHasEpisodes, bool ScratchUsable, bool? ReplaceOriginals);

/// <summary>
/// The first-run steps and the rules between them: what each needs to move on, where Back
/// leads and what Skip ends. It holds no files and no window.
/// </summary>
public sealed class FirstRunFlow
{
    public FirstRunStep Step { get; private set; }

    public bool IsRunning => Step != FirstRunStep.Off;

    /// <summary>A card is up over the deck.</summary>
    public bool IsCard => Step is FirstRunStep.Tools or FirstRunStep.Library or FirstRunStep.Scratch or FirstRunStep.Originals;

    public bool IsTour => Step is FirstRunStep.TourLibrary or FirstRunStep.TourEncode or FirstRunStep.TourSavings;

    /// <summary>
    /// The library card is the first one that asks a question, so Back starts after it. The
    /// self-test moves on by itself and has nothing to go back to.
    /// </summary>
    public bool CanGoBack => Step is FirstRunStep.Scratch or FirstRunStep.Originals;

    /// <summary>
    /// True when a start should open the cards: a first start with no library, or a setup that
    /// was quit before its originals choice. Settings written by an older version, with a
    /// library and no flag, count as set up.
    /// </summary>
    public static bool NeedsSetup(ShrinkRaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.FirstRunCompleted switch
        {
            true => false,
            false => !settings.FirstEpisodePending,
            null => string.IsNullOrWhiteSpace(settings.LibraryRoot),
        };
    }

    /// <summary>True while the next run is the trial: it stops after one episode.</summary>
    public static bool IsFirstEpisodePending(ShrinkRaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings is { FirstRunCompleted: false, FirstEpisodePending: true };
    }

    public static bool CanAdvance(FirstRunStep step, FirstRunFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return step switch
        {
            FirstRunStep.Off => false,
            FirstRunStep.Tools => facts.ToolsFound,
            FirstRunStep.Library => facts.LibraryHasEpisodes,
            FirstRunStep.Scratch => facts.ScratchUsable,
            FirstRunStep.Originals => facts.ReplaceOriginals is not null,
            _ => true,
        };
    }

    /// <summary>Opens the first card.</summary>
    public void Begin() => Step = FirstRunStep.Tools;

    /// <summary>Opens the tour on its own, as Setup's Show the tour does.</summary>
    public void BeginTour() => Step = FirstRunStep.TourLibrary;

    /// <summary>Moves to the next step when this one has what it needs. The last tour stop ends the flow.</summary>
    /// <returns>False when the step cannot move on yet.</returns>
    public bool Next(FirstRunFacts facts)
    {
        if (!CanAdvance(Step, facts))
        {
            return false;
        }

        Step = Step == FirstRunStep.TourSavings ? FirstRunStep.Off : Step + 1;
        return true;
    }

    public bool Back()
    {
        if (!CanGoBack)
        {
            return false;
        }

        Step--;
        return true;
    }

    /// <summary>Ends the tour from any stop. A card cannot be skipped.</summary>
    public bool Skip()
    {
        if (!IsTour)
        {
            return false;
        }

        Step = FirstRunStep.Off;
        return true;
    }

    /// <summary>Closes setup, for a re-run the user backs out of.</summary>
    public void Cancel() => Step = FirstRunStep.Off;
}
