using ShrinkRay.Core.Encode;
using ShrinkRay.Core.Media;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Core.Processes;
using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Swap;
using ShrinkRay.Core.Tests.Support;
using ShrinkRay.Core.Verification;

namespace ShrinkRay.Core.Tests.Pipeline;

public class EpisodePipelineTests
{
    private const string Scratch = @"C:\Scratch";
    private const string Episode = @"X:\TV\Example Show\Season 01\Example Show S01E01.mkv";
    private const string OriginalBytes = "0123456789012345678901234567890123456789"; // 40 bytes

    private sealed class Rig
    {
        public InMemoryFileSystem Fs { get; } = new();
        public InMemoryJournalStore Store { get; } = new();
        public FakeProbe Probe { get; } = new();
        public FakeEncoder Encoder { get; }
        public FakeFontRestorer Fonts { get; }
        public FakeDecoder Decoder { get; } = new();
        public ShrinkRaySettings Settings { get; } = new() { ScratchFolder = Scratch };
        public SwapJournal Journal { get; }
        public EpisodePipeline Pipeline { get; }

        public Rig(MediaInfo source, MediaInfo encodedOutput, int encodedBytes = 20)
        {
            Fs.Write(Episode, OriginalBytes);
            Probe.Results[Episode] = source with { FileName = Episode };
            Encoder = new FakeEncoder(Fs, Probe, encodedOutput, encodedBytes);
            Fonts = new FakeFontRestorer(Fs, Probe);
            Journal = new SwapJournal(Store);
            var recovery = new SwapRecovery(Fs, Journal, new FakeFinalCheck());
            Pipeline = new EpisodePipeline(Fs, Probe, Encoder, Fonts, Decoder, Journal,
                new SwapExecutor(Fs, Journal, recovery), Settings);
        }

        public string Work => EpisodePipeline.WorkFolder(Scratch, Episode);

        public List<EpisodeProgress> Reports { get; } = [];

        /// <summary>Each phase once, in the order reported.</summary>
        public IEnumerable<EpisodePhase> Phases =>
            Reports.Select(r => r.Phase).Where((p, i) => i == 0 || Reports[i - 1].Phase != p);

        public Task<PipelineResult> RunAsync(CancellationToken ct = default) =>
            Pipeline.RunAsync(Episode, new ListProgress(Reports), ct);
    }

    private static MediaInfo Source() => MediaBuilder.Mkv().Duration(1400).Video().Audio("flac").Subtitle("ass").Build();

    private static MediaInfo Output() => MediaBuilder.Mkv().Duration(1400).Video("hevc").Audio("aac").Subtitle("ass").Build();

    [Fact]
    public async Task Good_encode_replaces_the_original_and_cleans_scratch()
    {
        var rig = new Rig(Source(), Output());

        var result = await rig.RunAsync();

        Assert.Equal(PipelineOutcome.Replaced, result.Outcome);
        Assert.Equal(20, rig.Fs.GetFileSize(Episode));
        Assert.DoesNotContain(rig.Fs.Files, f => f.StartsWith(Scratch, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            [
                SwapState.Queued, SwapState.CopiedToScratch, SwapState.Encoded, SwapState.Verified,
                SwapState.UploadedTemp, SwapState.OriginalRenamedAside, SwapState.Swapped, SwapState.OriginalDeleted,
            ],
            rig.Store.StatesFor(Episode));
    }

    [Fact]
    public async Task Captions_in_the_source_video_allow_one_extra_subtitle_track()
    {
        var output = MediaBuilder.Mkv().Duration(1400).Video("hevc").Audio("aac").Subtitle("ass").Subtitle("ass").Build();
        var rig = new Rig(Source(), output);
        var localCopy = Path.Combine(rig.Work, "source.mkv");
        rig.Probe.Captioned.Add(localCopy);

        var result = await rig.RunAsync();

        Assert.Equal(PipelineOutcome.Replaced, result.Outcome);
        Assert.Equal([localCopy], rig.Probe.CaptionChecks);
    }

    [Fact]
    public async Task Extra_subtitle_track_fails_a_source_without_captions()
    {
        var output = MediaBuilder.Mkv().Duration(1400).Video("hevc").Audio("aac").Subtitle("ass").Subtitle("ass").Build();
        var rig = new Rig(Source(), output);

        var result = await rig.RunAsync();

        Assert.Equal(PipelineOutcome.Failed, result.Outcome);
        Assert.Contains("Subtitle streams: expected 1, output has 2", result.Message);
        Assert.Equal(OriginalBytes, rig.Fs.Read(Episode));
    }

    [Fact]
    public async Task Each_phase_is_reported_as_it_starts()
    {
        var rig = new Rig(Source(), Output());

        await rig.RunAsync();

        Assert.Equal(
            [
                EpisodePhase.Probing, EpisodePhase.Copying, EpisodePhase.Encoding,
                EpisodePhase.Verifying, EpisodePhase.Uploading, EpisodePhase.Swapping,
            ],
            rig.Phases);
        var encode = rig.Reports.Where(r => r.Encode is not null).ToList();
        Assert.NotEmpty(encode);
        Assert.All(encode, r => Assert.Equal(EpisodePhase.Encoding, r.Phase));
    }

    [Fact]
    public async Task Font_restore_is_reported_between_encode_and_verify()
    {
        var source = MediaBuilder.Mkv().Duration(1400).Video().Audio("aac").Font().Build();
        var encoded = MediaBuilder.Mkv().Duration(1400).Video("hevc").Audio("aac").Build();
        var rig = new Rig(source, encoded);

        await rig.RunAsync();

        Assert.Equal(
            [
                EpisodePhase.Probing, EpisodePhase.Copying, EpisodePhase.Encoding, EpisodePhase.RestoringFonts,
                EpisodePhase.Verifying, EpisodePhase.Uploading, EpisodePhase.Swapping,
            ],
            rig.Phases);
    }

    [Fact]
    public async Task A_kept_original_never_reports_an_upload()
    {
        var rig = new Rig(Source(), Output(), encodedBytes: 38);

        await rig.RunAsync();

        Assert.Equal([EpisodePhase.Probing, EpisodePhase.Copying, EpisodePhase.Encoding, EpisodePhase.Verifying], rig.Phases);
    }

    [Fact]
    public async Task Encoder_reads_the_scratch_copy_not_the_share()
    {
        var rig = new Rig(Source(), Output());

        await rig.RunAsync();

        var plan = Assert.Single(rig.Encoder.Plans);
        Assert.StartsWith(rig.Work, plan.InputPath);
        Assert.StartsWith(rig.Work, plan.OutputPath);
        Assert.EndsWith(".mkv", plan.OutputPath);
    }

    [Fact]
    public async Task Show_override_reaches_the_encoder()
    {
        var rig = new Rig(Source(), Output());
        rig.Settings.LibraryRoot = @"X:\TV";
        rig.Settings.ShowCqOverrides["Example Show"] = 25;

        await rig.RunAsync();

        var args = Assert.Single(rig.Encoder.Plans).Arguments.ToList();
        Assert.Equal("25", args[args.IndexOf("-q") + 1]);
    }

    [Fact]
    public async Task Other_shows_get_the_default_cq()
    {
        var rig = new Rig(Source(), Output());
        rig.Settings.ShowCqOverrides["Different Show"] = 25;

        await rig.RunAsync();

        var args = Assert.Single(rig.Encoder.Plans).Arguments.ToList();
        Assert.Equal("28", args[args.IndexOf("-q") + 1]);
    }

    [Fact]
    public async Task Fonts_are_restored_before_verification()
    {
        var source = MediaBuilder.Mkv().Duration(1400).Video().Audio("aac").Font().Build();
        var encoded = MediaBuilder.Mkv().Duration(1400).Video("hevc").Audio("aac").Build();
        var rig = new Rig(source, encoded);

        var result = await rig.RunAsync();

        Assert.Equal(PipelineOutcome.Replaced, result.Outcome);
        Assert.Single(rig.Fonts.Plans);
        Assert.Contains(SwapState.FontsRestored, rig.Store.StatesFor(Episode));
    }

    [Fact]
    public async Task Fonts_are_not_restored_when_handbrake_kept_them()
    {
        var source = MediaBuilder.Mkv().Duration(1400).Video().Audio("aac").Font("A.ttf").Font("B.ttf").Build();
        var encoded = MediaBuilder.Mkv().Duration(1400).Video("hevc").Audio("aac").Font("A.ttf").Font("B.ttf").Build();
        var rig = new Rig(source, encoded);

        var result = await rig.RunAsync();

        Assert.Equal(PipelineOutcome.Replaced, result.Outcome);
        Assert.Empty(rig.Fonts.Plans);
        Assert.DoesNotContain(SwapState.FontsRestored, rig.Store.StatesFor(Episode));
    }

    [Fact]
    public async Task Fonts_a_restore_failed_to_copy_fail_verification()
    {
        var source = MediaBuilder.Mkv().Duration(1400).Video().Audio("aac").Font().Build();
        var encoded = MediaBuilder.Mkv().Duration(1400).Video("hevc").Audio("aac").Build();
        var rig = new Rig(source, encoded);
        rig.Fonts.Broken = true;

        var result = await rig.RunAsync();

        Assert.Equal(PipelineOutcome.Failed, result.Outcome);
        Assert.Contains("Attachments: expected 1, output has 0", result.Message);
        Assert.Equal(OriginalBytes, rig.Fs.Read(Episode));
    }

    [Fact]
    public async Task Low_savings_keeps_the_original()
    {
        var rig = new Rig(Source(), Output(), encodedBytes: 38);

        var result = await rig.RunAsync();

        Assert.Equal(PipelineOutcome.KeptOriginal, result.Outcome);
        Assert.Equal(OriginalBytes, rig.Fs.Read(Episode));
        Assert.Equal(SwapState.KeptOriginal, rig.Journal.CurrentState(Episode));
        Assert.DoesNotContain(rig.Fs.Files, f => f.StartsWith(Scratch, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Failed_decode_keeps_the_original_and_the_scratch_files()
    {
        var rig = new Rig(Source(), Output());
        rig.Decoder.Clean = false;

        var result = await rig.RunAsync();

        Assert.Equal(PipelineOutcome.Failed, result.Outcome);
        Assert.Contains("Full decode reported errors", result.Message);
        Assert.Equal(OriginalBytes, rig.Fs.Read(Episode));
        Assert.Equal(SwapState.Failed, rig.Journal.CurrentState(Episode));
        Assert.Contains(rig.Fs.Files, f => f.StartsWith(rig.Work, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Replace_off_leaves_the_original_and_the_encode()
    {
        var rig = new Rig(Source(), Output());
        rig.Settings.ReplaceOriginals = false;

        var result = await rig.RunAsync();

        Assert.Equal(PipelineOutcome.KeptOriginal, result.Outcome);
        Assert.Equal(OriginalBytes, rig.Fs.Read(Episode));
        Assert.DoesNotContain(rig.Store.StatesFor(Episode), s => s == SwapState.UploadedTemp);
        Assert.True(rig.Fs.FileExists(Path.Combine(rig.Work, "encoded.mkv")));
    }

    [Fact]
    public async Task Ineligible_file_is_skipped_without_a_copy()
    {
        var rig = new Rig(Fixtures.Probe("mkv_hevc"), Output());

        var result = await rig.RunAsync();

        Assert.Equal(PipelineOutcome.Skipped, result.Outcome);
        Assert.Equal("Already hevc", result.Message);
        Assert.Empty(rig.Encoder.Plans);
        Assert.Single(rig.Fs.Files);
    }

    [Fact]
    public async Task Encoder_error_fails_the_file_and_keeps_the_original()
    {
        var rig = new Rig(Source(), Output());
        rig.Encoder.Error = new ExternalToolException("HandBrakeCLI exited with code 2");

        var result = await rig.RunAsync();

        Assert.Equal(PipelineOutcome.Failed, result.Outcome);
        Assert.Contains("code 2", result.Message);
        Assert.Equal(OriginalBytes, rig.Fs.Read(Episode));
        Assert.Equal(SwapState.Failed, rig.Journal.CurrentState(Episode));
    }

    [Fact]
    public async Task Skip_or_stop_marks_the_file_cancelled_and_clears_its_scratch_folder()
    {
        var rig = new Rig(Source(), Output());
        using var stop = new CancellationTokenSource();
        rig.Encoder.CancelMidway = stop;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.RunAsync(stop.Token));

        Assert.Equal(SwapState.Cancelled, rig.Journal.CurrentState(Episode));
        Assert.DoesNotContain(SwapState.Failed, rig.Store.StatesFor(Episode));
        Assert.Equal(OriginalBytes, rig.Fs.Read(Episode));
        Assert.DoesNotContain(rig.Fs.Files, f => f.StartsWith(Scratch, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_cancelled_file_runs_again()
    {
        var rig = new Rig(Source(), Output());
        using (var stop = new CancellationTokenSource())
        {
            rig.Encoder.CancelMidway = stop;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.RunAsync(stop.Token));
        }

        rig.Encoder.CancelMidway = null;
        var result = await rig.RunAsync();

        Assert.Equal(PipelineOutcome.Replaced, result.Outcome);
    }

    [Fact]
    public async Task Stop_during_the_upload_removes_the_temp_file_and_marks_the_file_cancelled()
    {
        var rig = new Rig(Source(), Output());
        using var stop = new CancellationTokenSource();
        rig.Decoder.OnDecode = stop.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.RunAsync(stop.Token));

        Assert.Equal(SwapState.Cancelled, rig.Journal.CurrentState(Episode));
        Assert.Contains(SwapState.UploadedTemp, rig.Store.StatesFor(Episode));
        Assert.Equal(OriginalBytes, rig.Fs.Read(Episode));
        Assert.False(rig.Fs.FileExists(SwapPaths.Temp(Episode)));
        Assert.False(rig.Fs.FileExists(SwapPaths.Backup(Episode)));
        Assert.DoesNotContain(rig.Fs.Files, f => f.StartsWith(Scratch, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_cancellation_nobody_asked_for_still_fails_the_file()
    {
        var rig = new Rig(Source(), Output());
        rig.Encoder.Error = new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.RunAsync());

        Assert.Equal(SwapState.Failed, rig.Journal.CurrentState(Episode));
        Assert.Equal(OriginalBytes, rig.Fs.Read(Episode));
    }

    [Fact]
    public void Startup_removes_scratch_left_by_cancelled_files_only()
    {
        var fs = new InMemoryFileSystem();
        var journal = new SwapJournal(new InMemoryJournalStore());
        var cancelled = Episode;
        var failed = Episode.Replace("S01E01", "S01E02");
        foreach (var (path, end) in new[] { (cancelled, SwapState.Cancelled), (failed, SwapState.Failed) })
        {
            journal.Record(path, SwapState.Queued);
            journal.Record(path, SwapState.CopiedToScratch);
            journal.Record(path, end);
            fs.Write(Path.Combine(EpisodePipeline.WorkFolder(Scratch, path), "encoded.mkv"), "partial");
        }

        EpisodePipeline.RemoveCancelledWork(fs, journal, Scratch);

        Assert.False(fs.FileExists(Path.Combine(EpisodePipeline.WorkFolder(Scratch, cancelled), "encoded.mkv")));
        Assert.True(fs.FileExists(Path.Combine(EpisodePipeline.WorkFolder(Scratch, failed), "encoded.mkv")));
    }

    [Fact]
    public async Task A_failed_file_can_run_again()
    {
        var rig = new Rig(Source(), Output());
        rig.Decoder.Clean = false;
        await rig.RunAsync();
        rig.Decoder.Clean = true;

        var result = await rig.RunAsync();

        Assert.Equal(PipelineOutcome.Replaced, result.Outcome);
    }

    [Fact]
    public async Task Missing_scratch_folder_is_refused()
    {
        var rig = new Rig(Source(), Output());
        rig.Settings.ScratchFolder = "";

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.RunAsync());
    }

    [Fact]
    public async Task File_left_mid_swap_is_refused_until_recovery()
    {
        var rig = new Rig(Source(), Output());
        foreach (var s in new[] { SwapState.Queued, SwapState.CopiedToScratch, SwapState.Encoded, SwapState.Verified, SwapState.UploadedTemp })
        {
            rig.Journal.Record(Episode, s);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.RunAsync());
        Assert.Equal(OriginalBytes, rig.Fs.Read(Episode));
    }

    [Fact]
    public void Work_folders_differ_per_episode_and_stay_short()
    {
        var a = EpisodePipeline.WorkFolder(Scratch, Episode);
        var b = EpisodePipeline.WorkFolder(Scratch, Episode.Replace("S01E01", "S01E02"));
        var c = EpisodePipeline.WorkFolder(Scratch, Episode.ToUpperInvariant());
        var longName = EpisodePipeline.WorkFolder(Scratch, @"X:\TV\" + new string('a', 200) + ".mkv");

        Assert.NotEqual(a, b);
        Assert.Equal(a.ToUpperInvariant(), c.ToUpperInvariant());
        Assert.StartsWith(Scratch + @"\Example Show S01E01-", a);
        Assert.True(Path.GetFileName(longName).Length <= 53);
    }

    public sealed class FakeProbe : IMediaProbe
    {
        public Dictionary<string, MediaInfo> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Files whose video carries closed captions, as the frame check would find them.</summary>
        public HashSet<string> Captioned { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> CaptionChecks { get; } = [];

        public Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(Results[path] with { FileName = path });

        public Task<MediaInfo> DetectClosedCaptionsAsync(string path, MediaInfo info, CancellationToken cancellationToken)
        {
            CaptionChecks.Add(path);
            return Task.FromResult(info with { HasClosedCaptions = info.HasClosedCaptions || Captioned.Contains(path) });
        }
    }

    public sealed class FakeEncoder(InMemoryFileSystem fs, FakeProbe probe, MediaInfo output, int bytes) : IVideoEncoder
    {
        public List<EncodePlan> Plans { get; } = [];
        public Exception? Error { get; set; }

        /// <summary>Writes half an output, then presses Stop the way the deck does: through the run's token.</summary>
        public CancellationTokenSource? CancelMidway { get; set; }

        public Task EncodeAsync(EncodePlan plan, IProgress<EncodeProgress>? progress, CancellationToken cancellationToken)
        {
            Plans.Add(plan);
            if (Error is not null)
            {
                throw Error;
            }

            if (CancelMidway is { } stop)
            {
                fs.Write(plan.OutputPath, "half");
                stop.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            Assert.True(fs.FileExists(plan.InputPath));
            progress?.Report(new EncodeProgress(1, 1, 50, 300, 300, null));
            fs.Write(plan.OutputPath, new string('h', bytes));
            probe.Results[plan.OutputPath] = output;
            return Task.CompletedTask;
        }
    }

    public sealed class FakeFontRestorer(InMemoryFileSystem fs, FakeProbe probe) : IFontRestorer
    {
        public List<FontRestorePlan> Plans { get; } = [];

        /// <summary>Writes the remux without copying any fonts in.</summary>
        public bool Broken { get; set; }

        public Task RestoreAsync(FontRestorePlan plan, CancellationToken cancellationToken)
        {
            Plans.Add(plan);
            fs.Write(plan.OutputPath, fs.Read(plan.EncodedPath));
            var encoded = probe.Results[plan.EncodedPath];
            if (Broken)
            {
                probe.Results[plan.OutputPath] = encoded;
                return Task.CompletedTask;
            }

            var fonts = probe.Results.Values.First(i => i.AttachmentStreams.Any()).AttachmentStreams;
            probe.Results[plan.OutputPath] = encoded with { Streams = [.. encoded.Streams, .. fonts] };
            return Task.CompletedTask;
        }
    }

    private sealed class ListProgress(List<EpisodeProgress> reports) : IProgress<EpisodeProgress>
    {
        public void Report(EpisodeProgress value) => reports.Add(value);
    }

    public sealed class FakeDecoder : IDecodeChecker
    {
        public bool Clean { get; set; } = true;

        /// <summary>Runs during the check, such as a Stop that lands as the upload begins.</summary>
        public Action? OnDecode { get; set; }

        public Task<bool> DecodesCleanlyAsync(string path, CancellationToken cancellationToken)
        {
            OnDecode?.Invoke();
            return Task.FromResult(Clean);
        }
    }
}
