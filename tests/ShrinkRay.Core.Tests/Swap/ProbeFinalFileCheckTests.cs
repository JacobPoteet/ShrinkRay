using ShrinkRay.Core.Media;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Swap;
using ShrinkRay.Core.Tests.Pipeline;
using ShrinkRay.Core.Tests.Support;

namespace ShrinkRay.Core.Tests.Swap;

public class ProbeFinalFileCheckTests
{
    private const string Final = @"X:\TV\Example Show\Season 01\Example Show S01E01.mkv";
    private static readonly string Backup = SwapPaths.Backup(Final);

    private static (ProbeFinalFileCheck Check, EpisodePipelineTests.FakeDecoder Decoder) Build(
        MediaInfo output, int finalBytes = 50, bool backupHasCaptions = false)
    {
        var fs = new InMemoryFileSystem();
        fs.Write(Backup, new string('o', 100));
        fs.Write(Final, new string('n', finalBytes));
        var probe = new EpisodePipelineTests.FakeProbe();
        probe.Results[Backup] = MediaBuilder.Mkv().Duration(1400).Video().Audio("ac3").Build();
        probe.Results[Final] = output;
        if (backupHasCaptions)
        {
            probe.Captioned.Add(Backup);
        }

        var decoder = new EpisodePipelineTests.FakeDecoder();
        return (new ProbeFinalFileCheck(fs, probe, decoder, new ShrinkRaySettings()), decoder);
    }

    [Fact]
    public async Task Captioned_original_allows_the_caption_track_in_the_final_file()
    {
        var withCaptions = MediaBuilder.Mkv().Duration(1400).Video("hevc").Audio("ac3").Subtitle("ass").Build();

        var (check, _) = Build(withCaptions, backupHasCaptions: true);
        Assert.True(await check.VerifiesAsync(Final, Backup, CancellationToken.None));

        var (plain, _) = Build(withCaptions);
        Assert.False(await plain.VerifiesAsync(Final, Backup, CancellationToken.None));
    }

    private static MediaInfo GoodOutput() => MediaBuilder.Mkv().Duration(1400).Video("hevc").Audio("ac3").Build();

    [Fact]
    public async Task Good_final_file_verifies()
    {
        var (check, _) = Build(GoodOutput());

        Assert.True(await check.VerifiesAsync(Final, Backup, CancellationToken.None));
    }

    [Fact]
    public async Task Decode_errors_fail_the_check()
    {
        var (check, decoder) = Build(GoodOutput());
        decoder.Clean = false;

        Assert.False(await check.VerifiesAsync(Final, Backup, CancellationToken.None));
    }

    [Fact]
    public async Task Truncated_final_file_fails_the_check()
    {
        var (check, _) = Build(GoodOutput() with { DurationSeconds = 600 });

        Assert.False(await check.VerifiesAsync(Final, Backup, CancellationToken.None));
    }

    [Fact]
    public async Task Probe_error_fails_the_check()
    {
        var (check, _) = Build(GoodOutput());

        Assert.False(await check.VerifiesAsync(Final + ".missing", Backup, CancellationToken.None));
    }
}
