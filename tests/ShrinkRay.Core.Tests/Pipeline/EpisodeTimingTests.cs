using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Processes;

namespace ShrinkRay.Core.Tests.Pipeline;

public class EpisodeTimingTests
{
    private static readonly DateTime Start = new(2026, 11, 26, 1, 0, 0);

    // 60 MB to copy at 60 MB/s, 1,650 frames at 165 and 330 fps, 50 MB to upload at 50 MB/s.
    private static readonly EpisodeEstimate Timing = EpisodeEstimate.For(
        1_650, 60_000_000, 50_000_000, new PhaseRates { CheckFps = 330 });

    [Fact]
    public void Each_step_takes_its_share_of_the_whole_episode()
    {
        Assert.Equal(new EpisodeEstimate(1, 10, 5, 1), Timing);
        Assert.Equal(0, Timing.Fraction(EpisodePhase.Probing, 0.5));
        Assert.Equal(0.5 / 17, Timing.Fraction(EpisodePhase.Copying, 0.5), 9);
        Assert.Equal(6.0 / 17, Timing.Fraction(EpisodePhase.Encoding, 0.5), 9);
        Assert.Equal(11.0 / 17, Timing.Fraction(EpisodePhase.RestoringFonts, 0), 9);
        Assert.Equal(13.5 / 17, Timing.Fraction(EpisodePhase.Verifying, 0.5), 9);
        Assert.Equal(16.5 / 17, Timing.Fraction(EpisodePhase.Uploading, 0.5), 9);
        Assert.Equal(1, Timing.Fraction(EpisodePhase.Swapping, 0));
    }

    [Fact]
    public void A_step_never_starts_behind_where_the_one_before_ended()
    {
        var phases = Enum.GetValues<EpisodePhase>().Order().ToList();

        for (var i = 1; i < phases.Count; i++)
        {
            Assert.True(Timing.Fraction(phases[i], 0) >= Timing.Fraction(phases[i - 1], 1) - 1e-9, phases[i].ToString());
        }
    }

    [Fact]
    public void The_other_steps_left_shrink_as_they_pass()
    {
        Assert.Equal(7, Timing.OtherSecondsLeft(EpisodePhase.Probing, 0));
        Assert.Equal(6.5, Timing.OtherSecondsLeft(EpisodePhase.Copying, 0.5));
        Assert.Equal(6, Timing.OtherSecondsLeft(EpisodePhase.Encoding, 0.5));
        Assert.Equal(3.5, Timing.OtherSecondsLeft(EpisodePhase.Verifying, 0.5));
        Assert.Equal(0.5, Timing.OtherSecondsLeft(EpisodePhase.Uploading, 0.5));
        Assert.Equal(0, Timing.OtherSecondsLeft(EpisodePhase.Swapping, 0));
    }

    [Fact]
    public void Nothing_to_upload_and_an_unknown_frame_count_still_give_shares()
    {
        var keeping = EpisodeEstimate.For(1_650, 60_000_000, 0, new PhaseRates());
        var unknown = EpisodeEstimate.For(0, 60_000_000, 0, new PhaseRates());

        Assert.Equal(0, keeping.UploadSeconds);
        Assert.Equal(1, keeping.Fraction(EpisodePhase.Verifying, 1), 9);
        Assert.Equal(new EpisodeEstimate(1, 11, 4, 0), unknown);
    }

    [Fact]
    public void A_meter_teaches_the_rates_from_the_steps_it_timed()
    {
        var meter = new PhaseMeter(33_000, Start);
        var rates = new PhaseRates { CopyBytesPerSecond = 60, EncodeFps = 165, CheckFps = 460, UploadBytesPerSecond = 50 };

        rates = meter.Enter(EpisodePhase.Copying, Start, rates);
        meter.Moving(400);
        rates = meter.Enter(EpisodePhase.Encoding, Start.AddSeconds(10), rates);
        rates = meter.Enter(EpisodePhase.Verifying, Start.AddSeconds(410), rates);
        rates = meter.Enter(EpisodePhase.Uploading, Start.AddSeconds(510), rates);
        meter.Moving(900);
        rates = meter.Enter(EpisodePhase.Swapping, Start.AddSeconds(520), rates);

        Assert.Equal(50, rates.CopyBytesPerSecond);
        Assert.Equal((165 + 82.5) / 2, rates.EncodeFps);
        Assert.Equal((460 + 330.0) / 2, rates.CheckFps);
        Assert.Equal(70, rates.UploadBytesPerSecond);
    }

    [Fact]
    public void A_step_that_was_paused_or_over_in_a_moment_teaches_nothing()
    {
        var rates = new PhaseRates();
        var meter = new PhaseMeter(33_000, Start);
        meter.Enter(EpisodePhase.Encoding, Start, rates);
        meter.Interrupt();

        var paused = meter.Enter(EpisodePhase.Verifying, Start.AddHours(8), rates);
        var brief = meter.Enter(EpisodePhase.Uploading, Start.AddHours(8).AddSeconds(2), rates);

        Assert.Equal(rates, paused);
        Assert.Equal(rates, brief);
    }

    [Fact]
    public void Rates_save_and_load_and_bad_values_fall_back()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shrinkray-rates-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "rates.json");
        try
        {
            Assert.Equal(new PhaseRates(), PhaseRates.Load(path));

            var learned = new PhaseRates { EncodeFps = 120, CheckFps = 300 };
            learned.Save(path);
            Assert.Equal(learned, PhaseRates.Load(path));

            File.WriteAllText(path, "{\"encodeFps\":0,\"checkFps\":-4,\"copyBytesPerSecond\":123}");
            Assert.Equal(new PhaseRates { CopyBytesPerSecond = 123 }, PhaseRates.Load(path));

            File.WriteAllText(path, "not json");
            Assert.Equal(new PhaseRates(), PhaseRates.Load(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("out_time_us=1500000", 1.5)]
    [InlineData("out_time_us=0", 0.0)]
    public void The_decode_checks_progress_lines_read_as_running_time(string line, double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), FfmpegDecodeChecker.ParseDecodedTime(line));
    }

    [Theory]
    [InlineData("out_time_us=N/A")]
    [InlineData("out_time=00:00:01.500000")]
    [InlineData("frame=12")]
    [InlineData("progress=end")]
    public void Other_progress_lines_are_ignored(string line)
    {
        Assert.Null(FfmpegDecodeChecker.ParseDecodedTime(line));
    }
}
