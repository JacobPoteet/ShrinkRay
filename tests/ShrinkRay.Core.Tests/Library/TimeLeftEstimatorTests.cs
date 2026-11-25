using ShrinkRay.Core.Library;

namespace ShrinkRay.Core.Tests.Library;

public class TimeLeftEstimatorTests
{
    private static readonly DateTime Start = new(2026, 11, 24, 2, 0, 0);

    private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

    [Fact]
    public void Counts_down_with_the_clock_between_samples()
    {
        var e = new TimeLeftEstimator();
        e.Sample(Start, 100 * 3600, 100);

        Assert.Equal(Min(60), e.Left(Start));
        Assert.Equal(Min(45), e.Left(Start.AddMinutes(15)));
        Assert.Equal(45, e.DisplayMinutes(Start.AddMinutes(15)));
    }

    [Fact]
    public void Never_counts_below_zero()
    {
        var e = new TimeLeftEstimator();
        e.Sample(Start, 100 * 60, 100);

        Assert.Equal(TimeSpan.Zero, e.Left(Start.AddHours(1)));
    }

    [Fact]
    public void Noisy_speed_never_makes_the_display_go_up_and_back()
    {
        var e = new TimeLeftEstimator();
        var rng = new Random(7);
        const double trueFps = 100;
        var frames = (long)(trueFps * 2 * 3600);
        var now = Start;
        var last = int.MaxValue;
        var ups = new List<int>();

        // Two hours of work seen every two seconds, with speed readings 25% either side of the truth.
        for (var step = 0; frames > 0; step++)
        {
            now = Start.AddSeconds(step * 2);
            e.Sample(now, frames, trueFps * (1 + ((rng.NextDouble() - 0.5) * 0.5)));
            var shown = e.DisplayMinutes(now);

            // The first minute is the warm-up, when the first estimates settle.
            if (shown > last && step > 40)
            {
                ups.Add(shown - last);
            }

            last = shown;
            frames -= (long)(trueFps * 2);
        }

        // A correction may lift the display once or twice, by more than the flicker's minute.
        Assert.All(ups, up => Assert.True(up > 1, $"Went up by {up}."));
        Assert.True(ups.Count <= 2, $"Went up {ups.Count} times.");
    }

    [Fact]
    public void Stays_within_the_threshold_of_the_true_value()
    {
        var e = new TimeLeftEstimator();
        var rng = new Random(11);
        const double trueFps = 120;
        var frames = (long)(trueFps * 90 * 60);
        var worst = TimeSpan.Zero;

        for (var step = 0; step < 90 * 30; step++)
        {
            var now = Start.AddSeconds(step * 2);
            e.Sample(now, frames, trueFps * (1 + ((rng.NextDouble() - 0.5) * 0.4)));
            var truth = TimeSpan.FromSeconds(frames / trueFps);
            var error = (e.Left(now) - truth).Duration();
            worst = error > worst && step > 30 ? error : worst;
            frames -= (long)(trueFps * 2);
        }

        Assert.True(worst <= TimeLeftEstimator.ThresholdFor(Min(90)), $"Worst error was {worst}.");
    }

    [Fact]
    public void A_small_drift_never_shows()
    {
        var e = new TimeLeftEstimator();
        e.Sample(Start, 100 * 3600, 100);

        // The speed settles 4% lower. That is 2.4 minutes of an hour, under the 3 minute floor.
        e.Sample(Start, 100 * 3600, 96);
        e.Sample(Start.AddSeconds(1), 100 * 3600 - 100, 96);

        Assert.Equal(Min(60) - TimeSpan.FromSeconds(1), e.Left(Start.AddSeconds(1)));
    }

    [Fact]
    public void Reanchors_when_the_speed_really_changes()
    {
        var e = new TimeLeftEstimator();
        e.Sample(Start, 100 * 3600, 100);

        // The encode slows to a quarter speed for good. The smoothed speed follows and the anchor
        // moves once it is clearly wrong.
        var now = Start;
        var frames = 100L * 3600;
        var before = e.Left(now);
        var jumped = false;
        for (var s = 1; s <= 600; s++)
        {
            now = Start.AddSeconds(s);
            frames -= 25;
            e.Sample(now, frames, 25);
            if (e.Left(now) > before - TimeSpan.FromSeconds(s) + TimeSpan.FromMinutes(1))
            {
                jumped = true;
            }
        }

        Assert.True(jumped);
        var truth = TimeSpan.FromSeconds(frames / 25.0);
        Assert.True((e.Left(now) - truth).Duration() <= TimeLeftEstimator.ThresholdFor(truth));
    }

    [Fact]
    public void Freezes_while_paused_and_resumes_from_the_same_value()
    {
        var e = new TimeLeftEstimator();
        e.Sample(Start, 100 * 3600, 100);
        var at = Start.AddMinutes(10);
        e.SetFrozen(true, at);

        Assert.Equal(Min(50), e.Left(at));
        Assert.Equal(Min(50), e.Left(at.AddHours(3)));

        var resume = at.AddHours(3);
        e.SetFrozen(false, resume);

        Assert.Equal(Min(50), e.Left(resume));
        Assert.Equal(Min(40), e.Left(resume.AddMinutes(10)));
    }

    [Fact]
    public void An_episode_that_drops_out_pulls_the_counter_down_at_once()
    {
        var e = new TimeLeftEstimator();
        e.Sample(Start, 100 * 3600, 100);

        // One episode of 4 minutes is skipped. That is under the threshold, but a lower estimate is taken at a boundary.
        e.Rebase(Start.AddSeconds(30), (100L * 3600) - (100 * 4 * 60) - (100 * 30));

        Assert.True(e.Left(Start.AddSeconds(30)) < Min(56));
    }

    [Fact]
    public void A_higher_estimate_at_a_boundary_is_ignored_inside_the_threshold()
    {
        var e = new TimeLeftEstimator();
        e.Sample(Start, 100 * 3600, 100);
        var later = Start.AddMinutes(10);

        // 2 minutes more work than the countdown says, under the 5 minute threshold.
        e.Rebase(later, (100L * 52 * 60));

        Assert.Equal(Min(50), e.Left(later));
    }

    [Fact]
    public void A_higher_estimate_at_a_boundary_is_taken_once_past_the_threshold()
    {
        var e = new TimeLeftEstimator();
        e.Sample(Start, 100 * 3600, 100);
        var later = Start.AddMinutes(10);

        e.Rebase(later, 100L * 70 * 60);

        Assert.Equal(Min(70), e.Left(later));
    }

    [Fact]
    public void Runs_out_of_countdown_and_picks_up_the_real_estimate()
    {
        var e = new TimeLeftEstimator();
        e.Sample(Start, 100 * 600, 100);
        var late = Start.AddMinutes(11);

        e.Sample(late, 100 * 120, 100);

        Assert.Equal(Min(2), e.Left(late));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.2, 1)]
    [InlineData(59.5, 60)]
    [InlineData(60, 60)]
    [InlineData(60.5, 65)]
    [InlineData(85, 85)]
    [InlineData(83.1, 85)]
    [InlineData(124, 125)]
    public void Rounds_up_in_whole_minutes_and_in_fives_above_an_hour(double minutes, int shown)
    {
        Assert.Equal(shown, TimeLeftEstimator.RoundForDisplay(Min(minutes)));
    }

    [Theory]
    [InlineData(10, 3)]
    [InlineData(30, 3)]
    [InlineData(60, 6)]
    [InlineData(300, 30)]
    public void The_threshold_is_the_larger_of_three_minutes_and_a_tenth(double leftMinutes, double thresholdMinutes)
    {
        Assert.Equal(Min(thresholdMinutes), TimeLeftEstimator.ThresholdFor(Min(leftMinutes)));
    }
}
