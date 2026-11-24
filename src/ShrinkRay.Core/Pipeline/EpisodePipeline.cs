using System.Security.Cryptography;
using System.Text;
using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Processes;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Swap;
using ShrinkRay.Core.Verification;

namespace ShrinkRay.Core.Pipeline;

public enum PipelineOutcome
{
    Replaced,
    KeptOriginal,
    Skipped,
    Failed,
}

public sealed record PipelineResult(PipelineOutcome Outcome, string Message, VerificationResult? Verification = null);

/// <summary>Runs one episode end to end. <see cref="EpisodePipeline"/> is the real one.</summary>
public interface IEpisodeRunner
{
    /// <param name="progress">Told each phase as it starts, and HandBrake's progress during the encode.</param>
    Task<PipelineResult> RunAsync(string sourcePath, IProgress<EpisodeProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Takes one episode from the share through copy, encode, font restore, verification and swap,
/// journaling each step. Everything before the swap happens in the scratch folder.
/// </summary>
public sealed class EpisodePipeline : IEpisodeRunner
{
    public const string CancelledReason = "Skipped or stopped from the deck before the swap. The original was not touched.";

    /// <summary>Starts the journal reason for an encode kept in scratch because replacing originals is off.</summary>
    public const string ReplaceOffReason = "Replacing originals is off.";

    private readonly IFileSystem _fs;
    private readonly IMediaProbe _probe;
    private readonly IVideoEncoder _encoder;
    private readonly IFontRestorer _fonts;
    private readonly IDecodeChecker _decoder;
    private readonly SwapJournal _journal;
    private readonly SwapExecutor _swap;
    private readonly ShrinkRaySettings _settings;

    public EpisodePipeline(
        IFileSystem fs,
        IMediaProbe probe,
        IVideoEncoder encoder,
        IFontRestorer fonts,
        IDecodeChecker decoder,
        SwapJournal journal,
        SwapExecutor swap,
        ShrinkRaySettings settings)
    {
        _fs = fs;
        _probe = probe;
        _encoder = encoder;
        _fonts = fonts;
        _decoder = decoder;
        _journal = journal;
        _swap = swap;
        _settings = settings;
    }

    /// <exception cref="InvalidOperationException">No scratch folder is set, or the journal shows this file mid-run.</exception>
    public async Task<PipelineResult> RunAsync(string sourcePath, IProgress<EpisodeProgress>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ScratchFolder))
        {
            throw new InvalidOperationException("Set a scratch folder before encoding.");
        }

        var state = _journal.CurrentState(sourcePath);
        if (state is not (null or SwapState.Queued or SwapState.Failed or SwapState.KeptOriginal or SwapState.Skipped
            or SwapState.Cancelled or SwapState.OriginalDeleted))
        {
            throw new InvalidOperationException($"{sourcePath} is at {state} in the journal. Run recovery first.");
        }

        if (state != SwapState.Queued)
        {
            _journal.Record(sourcePath, SwapState.Queued);
        }

        var work = WorkFolder(_settings.ScratchFolder, sourcePath);
        try
        {
            return await RunStepsAsync(sourcePath, work, progress, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Skip or Stop. The share was not touched, so the file is cancelled rather than failed.
            SettleIfStillLocal(sourcePath, SwapState.Cancelled, CancelledReason);
            RemoveWorkIfSettled(sourcePath, work);
            throw;
        }
        catch (OperationCanceledException)
        {
            SettleIfStillLocal(sourcePath, SwapState.Failed, "Cancelled");
            RemoveWorkIfSettled(sourcePath, work);
            throw;
        }
        catch (Exception ex)
        {
            SettleIfStillLocal(sourcePath, SwapState.Failed, ex.Message);
            RemoveWorkIfSettled(sourcePath, work);
            return new PipelineResult(PipelineOutcome.Failed, ex.Message);
        }
    }

    /// <summary>
    /// True when a file's scratch folder holds nothing worth keeping: the file ended Failed,
    /// KeptOriginal, Skipped or Cancelled, with the original untouched on the share. A file
    /// mid-swap, or one recovery left for review, keeps its folder. So does the verified encode
    /// kept because replacing originals is off, since the deck says those stay in scratch.
    /// </summary>
    public static bool IsWorkDisposable(JournalEntry? entry) => entry?.State switch
    {
        SwapState.Failed or SwapState.Skipped or SwapState.Cancelled => true,
        SwapState.KeptOriginal => entry.Reason?.StartsWith(ReplaceOffReason, StringComparison.Ordinal) != true,
        _ => false,
    };

    /// <summary>
    /// Deletes the scratch folders of files the journal shows settled with nothing to keep (see
    /// <see cref="IsWorkDisposable"/>). The pipeline deletes them as each file ends; this catches
    /// a folder a locked file kept, or one left by an older version. Run it on startup after
    /// recovery, so a file recovery has just settled counts too.
    /// </summary>
    public static void RemoveSettledWork(IFileSystem fs, SwapJournal journal, string scratchFolder)
    {
        if (string.IsNullOrWhiteSpace(scratchFolder))
        {
            return;
        }

        foreach (var entry in journal.LatestEntries().Where(IsWorkDisposable))
        {
            try
            {
                fs.DeleteDirectory(WorkFolder(scratchFolder, entry.Path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still locked. The next start or the file's next run tries again.
            }
        }
    }

    /// <summary>A per-file folder in scratch, named so two episodes never share one.</summary>
    public static string WorkFolder(string scratchFolder, string sourcePath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath.ToUpperInvariant()));
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var safe = new string(stem.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        if (safe.Length > 40)
        {
            safe = safe[..40];
        }

        return Path.Combine(scratchFolder, $"{safe.TrimEnd(' ', '.')}-{Convert.ToHexString(hash, 0, 6).ToLowerInvariant()}");
    }

    private async Task<PipelineResult> RunStepsAsync(string sourcePath, string work, IProgress<EpisodeProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new EpisodeProgress(EpisodePhase.Probing));
        var source = await _probe.ProbeAsync(sourcePath, ct);
        var eligibility = Eligibility.Evaluate(source);
        if (!eligibility.IsEligible)
        {
            _journal.Record(sourcePath, SwapState.Skipped, eligibility.Message);
            return new PipelineResult(PipelineOutcome.Skipped, eligibility.Message);
        }

        var extension = Path.GetExtension(sourcePath);
        var localSource = Path.Combine(work, "source" + extension);
        var encoded = Path.Combine(work, "encoded" + extension);
        var withFonts = Path.Combine(work, "final" + extension);

        _fs.DeleteDirectory(work);
        _fs.CreateDirectory(work);

        _journal.Record(sourcePath, SwapState.CopiedToScratch);
        progress?.Report(new EpisodeProgress(EpisodePhase.Copying));
        await _fs.CopyFileAsync(sourcePath, localSource, ct);

        // The caption check decodes the first frames, so it reads the local copy rather than the share.
        source = await _probe.DetectClosedCaptionsAsync(localSource, source, ct);

        _journal.Record(sourcePath, SwapState.Encoded);
        var show = ShowFolder.NameFor(sourcePath, _settings.LibraryRoot);
        var plan = EncodePlanner.Build(source, localSource, encoded, _settings, show);
        progress?.Report(new EpisodeProgress(EpisodePhase.Encoding));
        var encodeProgress = progress is null ? null
            : new InlineProgress<EncodeProgress>(p => progress.Report(new EpisodeProgress(EpisodePhase.Encoding, p)));
        await _encoder.EncodeAsync(plan, encodeProgress, ct);

        var candidate = encoded;
        var output = await _probe.ProbeAsync(encoded, ct);
        var fontPlan = FontRestorePlanner.Plan(source, output, encoded, localSource, withFonts, _settings);
        if (fontPlan is not null)
        {
            _journal.Record(sourcePath, SwapState.FontsRestored);
            progress?.Report(new EpisodeProgress(EpisodePhase.RestoringFonts));
            await _fonts.RestoreAsync(fontPlan, ct);
            candidate = withFonts;
            output = await _probe.ProbeAsync(candidate, ct);
        }

        _journal.Record(sourcePath, SwapState.Verified);
        progress?.Report(new EpisodeProgress(EpisodePhase.Verifying));
        var decoded = await _decoder.DecodesCleanlyAsync(candidate, ct);
        var verification = OutputVerifier.Verify(
            new VerificationInput(source, output, _fs.GetFileSize(localSource), _fs.GetFileSize(candidate), decoded),
            _settings);

        switch (verification.Outcome)
        {
            case VerificationOutcome.Fail:
            {
                // The reasons go to the journal and the deck. The encode is gigabytes of scratch
                // nobody can use, so it goes too.
                var reasons = string.Join("; ", verification.Reasons);
                _journal.Record(sourcePath, SwapState.Failed, reasons);
                RemoveWorkIfSettled(sourcePath, work);
                return new PipelineResult(PipelineOutcome.Failed, reasons, verification);
            }

            case VerificationOutcome.KeepOriginal:
            {
                var reason = string.Join("; ", verification.Reasons);
                _journal.Record(sourcePath, SwapState.KeptOriginal, reason);
                RemoveWorkIfSettled(sourcePath, work);
                return new PipelineResult(PipelineOutcome.KeptOriginal, reason, verification);
            }
        }

        if (!_settings.ReplaceOriginals)
        {
            var message = $"{ReplaceOffReason} The verified encode is at {candidate}.";
            _journal.Record(sourcePath, SwapState.KeptOriginal, message);
            return new PipelineResult(PipelineOutcome.KeptOriginal, message, verification);
        }

        var steps = progress is null ? null : new InlineProgress<SwapState>(step =>
        {
            if (step == SwapState.UploadedTemp)
            {
                progress.Report(new EpisodeProgress(EpisodePhase.Uploading));
            }
            else if (step == SwapState.OriginalRenamedAside)
            {
                progress.Report(new EpisodeProgress(EpisodePhase.Swapping));
            }
        });
        var swap = await _swap.SwapAsync(sourcePath, candidate, verification, _settings, steps, ct);
        if (swap.Outcome != SwapOutcome.Swapped)
        {
            // Only once the swap has rolled back to Failed. A rollback that failed leaves the file mid-swap.
            RemoveWorkIfSettled(sourcePath, work);
            return new PipelineResult(PipelineOutcome.Failed, swap.Message ?? "Swap failed", verification);
        }

        TryDeleteWork(work);
        return new PipelineResult(PipelineOutcome.Replaced, swap.Message ?? "Replaced", verification);
    }

    /// <summary>Ends a file the share has not seen yet. A share step is left to the swap's own rollback.</summary>
    private void SettleIfStillLocal(string sourcePath, SwapState settled, string reason)
    {
        var state = _journal.CurrentState(sourcePath);
        if (state is SwapState.Queued || (state is { } s && s.IsLocalStep()))
        {
            _journal.Record(sourcePath, settled, reason);
        }
    }

    private void RemoveWorkIfSettled(string sourcePath, string work)
    {
        if (IsWorkDisposable(_journal.Latest(sourcePath)))
        {
            TryDeleteWork(work);
        }
    }

    private void TryDeleteWork(string work)
    {
        try
        {
            _fs.DeleteDirectory(work);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Scratch cleanup is best effort.
        }
    }
}
