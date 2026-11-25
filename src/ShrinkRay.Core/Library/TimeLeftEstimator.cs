namespace ShrinkRay.Core.Library;

/// <summary>
/// The show's time left, kept like a kitchen timer: it counts down with the clock from an anchor
/// and is corrected only when the anchor is clearly wrong. Speed is smoothed, so one slow stretch
/// of frames does not move the display. Pure: callers pass the time in, and tests use a fake clock.
/// </summary>
public sealed class TimeLeftEstimator
{
    /// <summary>Time constant of the speed average.</summary>
    public static readonly TimeSpan SmoothingWindow = TimeSpan.FromSeconds(60);

    /// <summary>The smallest gap between the countdown and a fresh estimate that moves the anchor.</summary>
    public static readonly TimeSpan MinThreshold = TimeSpan.FromMinutes(3);

    /// <summary>The share of the time left that also counts as a gap worth correcting.</summary>
    public const double ThresholdFraction = 0.10;

    /// <summary>The display counts in whole minutes up to this, then in <see cref="FarStepMinutes"/>.</summary>
    public const int FarAfterMinutes = 60;

    public const int FarStepMinutes = 5;

    /// <summary>Speed readings after which the first estimate is corrected once, whatever the threshold.</summary>
    public const int WarmupSamples = 30;

    private readonly double _fallbackFps;
    private double _speed;
    private int _samples;
    private TimeSpan _lastSampleClock;
    private bool _hasAnchor;
    private TimeSpan _anchorClock;
    private TimeSpan _anchorLeft;
    private TimeSpan _frozenTotal;
    private DateTime? _frozenAt;

    /// <param name="fallbackFps">The speed to use until an encode reports one.</param>
    public TimeLeftEstimator(double fallbackFps = SizeEstimate.MeasuredFps)
    {
        _fallbackFps = fallbackFps;
    }

    /// <summary>The smoothed speed in frames per second, or the fallback before any sample.</summary>
    public double Speed => _speed > 0 ? _speed : _fallbackFps;

    public bool IsFrozen => _frozenAt is not null;

    /// <summary>
    /// Takes a progress sample: the frames still to encode and, when known, the encode's speed.
    /// The anchor moves only when the fresh estimate is further off than the threshold.
    /// </summary>
    public void Sample(DateTime now, long framesLeft, double? fps)
    {
        var clock = ClockAt(now);
        if (fps is > 1)
        {
            if (_speed <= 0)
            {
                _speed = fps.Value;
            }
            else
            {
                // Early samples average evenly, so a lone odd first reading does not linger; later
                // ones fade out over the smoothing window.
                var dt = Math.Max(0, (clock - _lastSampleClock).TotalSeconds);
                var alpha = Math.Max(1 - Math.Exp(-dt / SmoothingWindow.TotalSeconds), 1.0 / (_samples + 1));
                _speed += alpha * (fps.Value - _speed);
            }

            _samples++;
            _lastSampleClock = clock;
        }

        // The anchor sticks once the countdown and a fresh estimate agree, so a bad first estimate
        // would stick too. One correction when the speed has settled keeps it honest.
        var settled = fps is > 1 && _samples == WarmupSamples;
        Consider(clock, framesLeft, movesDownFreely: false, force: settled);
    }

    /// <summary>
    /// Re-anchors at an episode boundary or a run start, from the smoothed speed. A lower estimate
    /// is taken at once, since skipped and finished episodes leave the work. A higher one is taken
    /// only past the threshold.
    /// </summary>
    public void Rebase(DateTime now, long framesLeft) => Consider(ClockAt(now), framesLeft, movesDownFreely: true);

    /// <summary>Freezes the countdown, or lets it run again from the same value.</summary>
    public void SetFrozen(bool frozen, DateTime now)
    {
        if (frozen && _frozenAt is null)
        {
            _frozenAt = now;
        }
        else if (!frozen && _frozenAt is { } since)
        {
            _frozenTotal += now - since;
            _frozenAt = null;
        }
    }

    /// <summary>The time left right now, counting down with the clock from the anchor.</summary>
    public TimeSpan Left(DateTime now)
    {
        if (!_hasAnchor)
        {
            return TimeSpan.Zero;
        }

        var left = _anchorLeft - (ClockAt(now) - _anchorClock);
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    /// <summary>The minutes to show: whole minutes, rounded up, in steps of five above an hour.</summary>
    public int DisplayMinutes(DateTime now) => RoundForDisplay(Left(now));

    /// <summary>Rounds a time up to the display's step. Never rounds down, so a countdown never reads ahead.</summary>
    public static int RoundForDisplay(TimeSpan left)
    {
        var minutes = (int)Math.Ceiling(left.TotalMinutes - 1e-9);
        if (minutes <= FarAfterMinutes)
        {
            return Math.Max(0, minutes);
        }

        return (minutes + FarStepMinutes - 1) / FarStepMinutes * FarStepMinutes;
    }

    /// <summary>How far the countdown may drift from a fresh estimate before the anchor moves.</summary>
    public static TimeSpan ThresholdFor(TimeSpan left)
    {
        var share = TimeSpan.FromTicks((long)(left.Ticks * ThresholdFraction));
        return share > MinThreshold ? share : MinThreshold;
    }

    private TimeSpan ClockAt(DateTime now)
    {
        var frozen = _frozenTotal + (_frozenAt is { } since ? now - since : TimeSpan.Zero);
        return TimeSpan.FromTicks(now.Ticks) - frozen;
    }

    private void Consider(TimeSpan clock, long framesLeft, bool movesDownFreely, bool force = false)
    {
        var fresh = framesLeft <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(framesLeft / Speed);
        if (!_hasAnchor)
        {
            Anchor(clock, fresh);
            return;
        }

        var current = _anchorLeft - (clock - _anchorClock);
        if (current < TimeSpan.Zero)
        {
            current = TimeSpan.Zero;
        }

        var gap = fresh - current;
        var runOut = current == TimeSpan.Zero && fresh > TimeSpan.Zero;
        var lower = movesDownFreely && gap < TimeSpan.Zero;
        if (force || runOut || lower || gap.Duration() > ThresholdFor(current))
        {
            Anchor(clock, fresh);
        }
    }

    private void Anchor(TimeSpan clock, TimeSpan left)
    {
        _hasAnchor = true;
        _anchorClock = clock;
        _anchorLeft = left;
    }
}
