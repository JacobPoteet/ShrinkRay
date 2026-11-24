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
    Task<PipelineResult> RunAsync(string sourcePath, IProgress<EncodeProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Takes one episode from the share through copy, encode, font restore, verification and swap,
/// journaling each step. Everything before the swap happens in the scratch folder.
/// </summary>
public sealed class EpisodePipeline : IEpisodeRunner
{
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
    public async Task<PipelineResult> RunAsync(string sourcePath, IProgress<EncodeProgress>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ScratchFolder))
        {
            throw new InvalidOperationException("Set a scratch folder before encoding.");
        }

        var state = _journal.CurrentState(sourcePath);
        if (state is not (null or SwapState.Queued or SwapState.Failed or SwapState.KeptOriginal or SwapState.Skipped or SwapState.OriginalDeleted))
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
        catch (OperationCanceledException)
        {
            FailIfStillLocal(sourcePath, "Cancelled");
            throw;
        }
        catch (Exception ex)
        {
            FailIfStillLocal(sourcePath, ex.Message);
            return new PipelineResult(PipelineOutcome.Failed, ex.Message);
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

    private async Task<PipelineResult> RunStepsAsync(string sourcePath, string work, IProgress<EncodeProgress>? progress, CancellationToken ct)
    {
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
        await _fs.CopyFileAsync(sourcePath, localSource, ct);

        _journal.Record(sourcePath, SwapState.Encoded);
        var show = ShowFolder.NameFor(sourcePath, _settings.LibraryRoot);
        var plan = EncodePlanner.Build(source, localSource, encoded, _settings, show);
        await _encoder.EncodeAsync(plan, progress, ct);

        var candidate = encoded;
        var output = await _probe.ProbeAsync(encoded, ct);
        var fontPlan = FontRestorePlanner.Plan(source, output, encoded, localSource, withFonts, _settings);
        if (fontPlan is not null)
        {
            _journal.Record(sourcePath, SwapState.FontsRestored);
            await _fonts.RestoreAsync(fontPlan, ct);
            candidate = withFonts;
            output = await _probe.ProbeAsync(candidate, ct);
        }

        _journal.Record(sourcePath, SwapState.Verified);
        var decoded = await _decoder.DecodesCleanlyAsync(candidate, ct);
        var verification = OutputVerifier.Verify(
            new VerificationInput(source, output, _fs.GetFileSize(localSource), _fs.GetFileSize(candidate), decoded),
            _settings);

        switch (verification.Outcome)
        {
            case VerificationOutcome.Fail:
            {
                // The work folder stays for inspection.
                var reasons = string.Join("; ", verification.Reasons);
                _journal.Record(sourcePath, SwapState.Failed, reasons);
                return new PipelineResult(PipelineOutcome.Failed, reasons, verification);
            }

            case VerificationOutcome.KeepOriginal:
            {
                var reason = string.Join("; ", verification.Reasons);
                _journal.Record(sourcePath, SwapState.KeptOriginal, reason);
                TryDeleteWork(work);
                return new PipelineResult(PipelineOutcome.KeptOriginal, reason, verification);
            }
        }

        if (!_settings.ReplaceOriginals)
        {
            var message = $"Replacing originals is off. The verified encode is at {candidate}.";
            _journal.Record(sourcePath, SwapState.KeptOriginal, message);
            return new PipelineResult(PipelineOutcome.KeptOriginal, message, verification);
        }

        var swap = await _swap.SwapAsync(sourcePath, candidate, verification, _settings, ct);
        if (swap.Outcome != SwapOutcome.Swapped)
        {
            return new PipelineResult(PipelineOutcome.Failed, swap.Message ?? "Swap failed", verification);
        }

        TryDeleteWork(work);
        return new PipelineResult(PipelineOutcome.Replaced, swap.Message ?? "Replaced", verification);
    }

    private void FailIfStillLocal(string sourcePath, string reason)
    {
        var state = _journal.CurrentState(sourcePath);
        if (state is SwapState.Queued || (state is { } s && s.IsLocalStep()))
        {
            _journal.Record(sourcePath, SwapState.Failed, reason);
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
