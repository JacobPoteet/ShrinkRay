using System.Text.Json;
using ShrinkRay.Core.Library;

namespace ShrinkRay.Core.Pipeline;

/// <summary>
/// How fast each step of an episode goes on this machine. The defaults come from timed runs of a
/// 2 GB episode over a home network: 37 s to copy, 399 s to encode and 142 s to check.
/// </summary>
public sealed record PhaseRates
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public double CopyBytesPerSecond { get; init; } = 60_000_000;

    public double EncodeFps { get; init; } = SizeEstimate.MeasuredFps;

    /// <summary>Frames a second the full decode check gets through, about three times the encode.</summary>
    public double CheckFps { get; init; } = 460;

    public double UploadBytesPerSecond { get; init; } = 50_000_000;

    /// <summary>
    /// The rates with <paramref name="phase"/>'s moved half way to a fresh measurement: bytes for
    /// the copy and the upload, frames for the encode and the check.
    /// </summary>
    public PhaseRates Learn(EpisodePhase phase, double amount, TimeSpan took)
    {
        if (!(amount > 0) || took <= TimeSpan.Zero)
        {
            return this;
        }

        var seen = amount / took.TotalSeconds;
        double Toward(double rate) => (rate + seen) / 2;
        return phase switch
        {
            EpisodePhase.Copying => this with { CopyBytesPerSecond = Toward(CopyBytesPerSecond) },
            EpisodePhase.Encoding => this with { EncodeFps = Toward(EncodeFps) },
            EpisodePhase.Verifying => this with { CheckFps = Toward(CheckFps) },
            EpisodePhase.Uploading => this with { UploadBytesPerSecond = Toward(UploadBytesPerSecond) },
            _ => this,
        };
    }

    /// <summary>A missing or unreadable file, or a rate that is not a positive number, gives the default.</summary>
    public static PhaseRates Load(string path)
    {
        var fallback = new PhaseRates();
        try
        {
            if (!File.Exists(path) || JsonSerializer.Deserialize<PhaseRates>(File.ReadAllText(path), Options) is not { } read)
            {
                return fallback;
            }

            static double Or(double value, double other) => double.IsFinite(value) && value > 0 ? value : other;
            return new PhaseRates
            {
                CopyBytesPerSecond = Or(read.CopyBytesPerSecond, fallback.CopyBytesPerSecond),
                EncodeFps = Or(read.EncodeFps, fallback.EncodeFps),
                CheckFps = Or(read.CheckFps, fallback.CheckFps),
                UploadBytesPerSecond = Or(read.UploadBytesPerSecond, fallback.UploadBytesPerSecond),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return fallback;
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>
/// What one episode's steps are expected to take, in seconds, and so how far through the whole
/// episode a step is. Fixed when the episode starts, so its percentage only moves forward.
/// </summary>
public sealed record EpisodeEstimate(double CopySeconds, double EncodeSeconds, double CheckSeconds, double UploadSeconds)
{
    /// <param name="frames">Frames to encode, or 0 when the scan could not tell.</param>
    /// <param name="outputBytes">The size expected to upload; 0 when nothing will be uploaded.</param>
    public static EpisodeEstimate For(long frames, long sourceBytes, long outputBytes, PhaseRates rates)
    {
        var copy = Math.Max(0, sourceBytes) / rates.CopyBytesPerSecond;
        var upload = Math.Max(0, outputBytes) / rates.UploadBytesPerSecond;

        // Without a frame count the encode and the check keep their usual size beside the copy.
        return frames > 0
            ? new EpisodeEstimate(copy, frames / rates.EncodeFps, frames / rates.CheckFps, upload)
            : new EpisodeEstimate(copy, copy * 11, copy * 4, upload);
    }

    public double TotalSeconds => CopySeconds + EncodeSeconds + CheckSeconds + UploadSeconds;

    /// <summary>Everything but the encode.</summary>
    public double OtherSeconds => CopySeconds + CheckSeconds + UploadSeconds;

    /// <summary>How far through the episode, 0 to 1.</summary>
    /// <param name="step">How far through <paramref name="phase"/>, 0 to 1.</param>
    public double Fraction(EpisodePhase phase, double step)
    {
        if (phase == EpisodePhase.Swapping)
        {
            return 1;
        }

        step = Math.Clamp(step, 0, 1);
        var done = phase switch
        {
            EpisodePhase.Copying => CopySeconds * step,
            EpisodePhase.Encoding => CopySeconds + (EncodeSeconds * step),
            EpisodePhase.RestoringFonts => CopySeconds + EncodeSeconds,
            EpisodePhase.Verifying => CopySeconds + EncodeSeconds + (CheckSeconds * step),
            EpisodePhase.Uploading => CopySeconds + EncodeSeconds + CheckSeconds + (UploadSeconds * step),
            _ => 0,
        };
        return TotalSeconds > 0 ? Math.Clamp(done / TotalSeconds, 0, 1) : 0;
    }

    /// <summary>The seconds of copying, checking and uploading still to come.</summary>
    public double OtherSecondsLeft(EpisodePhase phase, double step)
    {
        var left = 1 - Math.Clamp(step, 0, 1);
        return phase switch
        {
            EpisodePhase.Probing => OtherSeconds,
            EpisodePhase.Copying => (CopySeconds * left) + CheckSeconds + UploadSeconds,
            EpisodePhase.Encoding or EpisodePhase.RestoringFonts => CheckSeconds + UploadSeconds,
            EpisodePhase.Verifying => (CheckSeconds * left) + UploadSeconds,
            EpisodePhase.Uploading => UploadSeconds * left,
            _ => 0,
        };
    }
}

/// <summary>
/// Times one episode's steps so the rates learn from them. A step that was paused, or too short
/// to time, teaches nothing. Pure: callers pass the time in.
/// </summary>
public sealed class PhaseMeter
{
    /// <summary>The shortest step worth learning from.</summary>
    public static readonly TimeSpan Shortest = TimeSpan.FromSeconds(3);

    private readonly long _frames;
    private EpisodePhase _phase = EpisodePhase.Probing;
    private DateTime _since;
    private long _bytes;
    private bool _interrupted;

    public PhaseMeter(long frames, DateTime now)
    {
        _frames = frames;
        _since = now;
    }

    /// <summary>The size of the file the current copy or upload is moving.</summary>
    public void Moving(long bytes) => _bytes = bytes;

    /// <summary>The run paused or waited, so the current step's time no longer measures its speed.</summary>
    public void Interrupt() => _interrupted = true;

    /// <summary>
    /// Moves to <paramref name="next"/>, closing the step before it. Call it with
    /// <see cref="EpisodePhase.Swapping"/> when an episode ends well, to close its last step.
    /// </summary>
    /// <returns>The rates, taught by the step that closed.</returns>
    public PhaseRates Enter(EpisodePhase next, DateTime now, PhaseRates rates)
    {
        if (next == _phase)
        {
            return rates;
        }

        var took = now - _since;
        if (!_interrupted && took >= Shortest)
        {
            var amount = _phase is EpisodePhase.Copying or EpisodePhase.Uploading ? _bytes : _frames;
            rates = rates.Learn(_phase, amount, took);
        }

        _phase = next;
        _since = now;
        _bytes = 0;
        _interrupted = false;
        return rates;
    }
}
