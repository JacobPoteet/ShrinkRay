namespace ShrinkRay.Core.Swap;

/// <summary>
/// Where one file stands. The journal records each state before its step runs (write-ahead),
/// so an entry means "this step started". After a crash, recovery looks at the disk to learn
/// whether the step finished.
/// </summary>
public enum SwapState
{
    Queued,

    // Local steps. Nothing on the share changes during these.
    CopiedToScratch,
    Encoded,
    FontsRestored,
    Verified,

    // Share steps, in order.
    /// <summary>The verified file is copied beside the original as <c>name.shrinkray.tmp</c>.</summary>
    UploadedTemp,

    /// <summary>The original is renamed to <c>name.shrinkray.bak</c>.</summary>
    OriginalRenamedAside,

    /// <summary>The temp file is renamed to the original name.</summary>
    Swapped,

    /// <summary>The backup is deleted. The NAS recycle bin keeps a copy.</summary>
    OriginalDeleted,

    // Other endings.
    Failed,
    KeptOriginal,
    Skipped,
}

public static class SwapTransitions
{
    private static readonly Dictionary<SwapState, SwapState[]> Allowed = new()
    {
        [SwapState.Queued] = [SwapState.Queued, SwapState.CopiedToScratch, SwapState.Skipped, SwapState.Failed],
        [SwapState.CopiedToScratch] = [SwapState.Encoded, SwapState.Failed],
        [SwapState.Encoded] = [SwapState.FontsRestored, SwapState.Verified, SwapState.Failed],
        [SwapState.FontsRestored] = [SwapState.Verified, SwapState.Failed],
        [SwapState.Verified] = [SwapState.UploadedTemp, SwapState.KeptOriginal, SwapState.Failed],
        [SwapState.UploadedTemp] = [SwapState.OriginalRenamedAside, SwapState.Failed],
        [SwapState.OriginalRenamedAside] = [SwapState.Swapped, SwapState.Failed],
        [SwapState.Swapped] = [SwapState.OriginalDeleted, SwapState.Failed],
        // Recovery may confirm a delete that was cut short, or roll back if the new file fails its check.
        [SwapState.OriginalDeleted] = [SwapState.OriginalDeleted, SwapState.Failed, SwapState.Queued],
        [SwapState.Failed] = [SwapState.Queued],
        [SwapState.KeptOriginal] = [SwapState.Queued],
        [SwapState.Skipped] = [SwapState.Queued],
    };

    /// <param name="from">Null when the journal has no entry for the file yet.</param>
    public static bool IsAllowed(SwapState? from, SwapState to) =>
        from is { } current ? Allowed[current].Contains(to) : to == SwapState.Queued;

    public static bool IsLocalStep(this SwapState state) =>
        state is SwapState.CopiedToScratch or SwapState.Encoded or SwapState.FontsRestored or SwapState.Verified;

    /// <summary>A share step that may have been cut short: the file on the share may be mid-swap.</summary>
    public static bool IsShareStep(this SwapState state) =>
        state is SwapState.UploadedTemp or SwapState.OriginalRenamedAside or SwapState.Swapped or SwapState.OriginalDeleted;
}
