using ShrinkRay.Core.Library;
using ShrinkRay.Core.Pipeline;

namespace ShrinkRay.App.ViewModels;

/// <summary>What a run that ended on its own tells the tray.</summary>
/// <param name="NeedsAttention">The run stopped itself, or an episode failed.</param>
public sealed record RunReport(string Title, string Text, bool NeedsAttention);

/// <summary>Counts how a run's episodes ended, for the summary at its end.</summary>
public sealed class RunTally
{
    public int Shrunk { get; private set; }

    public int Kept { get; private set; }

    public int Failed { get; private set; }

    /// <summary>Episodes whose file changed on the share while they ran.</summary>
    public int Changed { get; private set; }

    public long SavedBytes { get; private set; }

    public bool IsEmpty => Shrunk + Kept + Failed + Changed == 0;

    /// <summary>Counts an episode. One skipped or stopped from the deck is not counted.</summary>
    public void Add(EpisodeFinished finished, long? originalBytes, long? savedBytes)
    {
        switch (finished.Result?.Outcome)
        {
            case PipelineOutcome.Replaced:
                Shrunk++;
                SavedBytes += savedBytes ?? 0;
                break;
            case PipelineOutcome.KeptOriginal:
                Kept++;
                break;
            case PipelineOutcome.Failed:
                Failed++;
                break;
            case PipelineOutcome.SourceChanged:
                Changed++;
                break;
        }
    }

    /// <summary>Such as <c>12 shrunk (14.20 GB saved), 1 failed</c>.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Shrunk > 0)
        {
            parts.Add($"{Shrunk} shrunk ({Format.Gigabytes(SavedBytes)} GB saved)");
        }

        if (Kept > 0)
        {
            parts.Add($"{Kept} kept");
        }

        if (Failed > 0)
        {
            parts.Add($"{Failed} failed");
        }

        if (Changed > 0)
        {
            parts.Add($"{Changed} changed on the share");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "nothing finished";
    }
}

public sealed partial class MainViewModel
{
    private RunTally _tally = new();

    /// <summary>Stop now was pressed during this run.</summary>
    private bool _stopAsked;

    /// <summary>A run ended by itself: its list ran out, or it stopped with episodes left.</summary>
    public event EventHandler<RunReport>? RunEnded;

    /// <summary>
    /// The free scratch space an episode needs is twice its size: room for the copy and the
    /// encode, with a margin. Called on the queue's thread before each episode.
    /// </summary>
    private string? NotEnoughSpace(string scratch, string path)
    {
        if (_s.FreeSpace(scratch) is not { } free || _s.FileSize(path) is not { } size || free >= size * 2)
        {
            return null;
        }

        return $"Scratch has {Format.Gigabytes(free)} GB free and {EpisodeName.Parse(path).Code} needs about "
            + $"{Format.Gigabytes(size * 2)} GB, so the run stopped. Free some space, then press Encode.";
    }

    /// <summary>
    /// Puts the run's summary on monitor A, and tells the tray when the run ended without being
    /// asked to.
    /// </summary>
    private void ReportRun(string? halted, bool asked)
    {
        if (halted is null && _tally.IsEmpty)
        {
            return;
        }

        var summary = _tally.Describe();
        _s.Log($"Run ended: {summary}." + (halted is null ? "" : $" {halted}"));
        Notice = halted is null
            ? $"Run {(asked ? "stopped" : "done")}: {summary}."
            : $"{halted.TrimEnd('.')}. So far: {summary}.";

        if (halted is not null)
        {
            RunEnded?.Invoke(this, new RunReport("ShrinkRay stopped the run", Notice, NeedsAttention: true));
        }
        else if (!asked)
        {
            RunEnded?.Invoke(this, new RunReport("ShrinkRay finished the run", $"{summary}.", NeedsAttention: _tally.Failed > 0));
        }
    }
}
