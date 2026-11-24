using System.Globalization;
using ShrinkRay.App.Mvvm;
using ShrinkRay.Core.Library;

namespace ShrinkRay.App.ViewModels;

public enum TrackKind
{
    Queued,
    Encoding,
    Shrunk,
    Kept,
    Failed,
    Skipped,
}

/// <summary>One episode line on the J-card.</summary>
public sealed class TrackViewModel : ObservableObject
{
    private TrackKind _kind;
    private double _progress;
    private string _statusText = "";
    private string _sizeText = "";
    private string _detail = "";

    private TrackViewModel(EpisodeRecord record)
    {
        Record = record;
        OriginalBytes = record.SizeBytes;
    }

    public EpisodeRecord Record { get; }

    public string Path => Record.Path;

    public string Code => Record.Code;

    public string Title => Record.Title;

    /// <summary>The size before ShrinkRay touched it: the ledger's original size for a shrunk file.</summary>
    public long OriginalBytes { get; private set; }

    /// <summary>The size on disk after a replace.</summary>
    public long? NewBytes { get; private set; }

    public long SavedBytes => NewBytes is { } n ? Math.Max(0, OriginalBytes - n) : 0;

    public TrackKind Kind
    {
        get => _kind;
        private set => Set(ref _kind, value);
    }

    /// <summary>0 to 1 while encoding; drives the tinted bar behind the row.</summary>
    public double Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    /// <summary>Gigabytes to two places, without the unit.</summary>
    public string SizeText
    {
        get => _sizeText;
        private set => Set(ref _sizeText, value);
    }

    /// <summary>Why it was skipped, kept or failed. Shown as a tooltip.</summary>
    public string Detail
    {
        get => _detail;
        private set => Set(ref _detail, value);
    }

    public bool IsQueueable => Kind is TrackKind.Queued or TrackKind.Failed;

    /// <param name="reason">The journal's reason for the file's last state.</param>
    /// <param name="at">When the journal recorded that state.</param>
    public static TrackViewModel From(EpisodeRecord record, EpisodeStatus status, LedgerEntry? ledger, string? reason, DateTimeOffset? at = null)
    {
        var track = new TrackViewModel(record);
        switch (status)
        {
            case EpisodeStatus.Shrunk:
                if (ledger is not null)
                {
                    track.OriginalBytes = ledger.OriginalBytes;
                }

                track.SetShrunk(record.SizeBytes);
                break;
            case EpisodeStatus.Kept:
                track.SetKept(null, reason ?? "Saved too little, so the original stayed.");
                break;
            case EpisodeStatus.Failed:
                track.SetFailed(reason ?? "The last attempt failed.", at);
                break;
            case EpisodeStatus.Skipped:
                track.SetSkipped(record.IsEfficientCodec ? record.VideoCodec.ToUpperInvariant() : "SKIP", record.SkipMessage);
                break;
            default:
                track.SetQueued();
                break;
        }

        return track;
    }

    public void SetQueued()
    {
        Kind = TrackKind.Queued;
        Progress = 0;
        StatusText = "QUEUED";
        SizeText = Format.Gigabytes(OriginalBytes);
        Detail = "";
    }

    /// <param name="ratio">Expected output size over source size, for the growing size figure.</param>
    /// <param name="status">A word for a phase other than the encode, such as COPY. Null shows the percent.</param>
    public void SetEncoding(double progress, double ratio, string? status = null)
    {
        Kind = TrackKind.Encoding;
        Progress = Math.Clamp(progress, 0, 1);
        StatusText = status ?? ((int)Math.Floor(Progress * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        SizeText = Format.Gigabytes((long)(OriginalBytes * ratio * Progress));
        Detail = "Working on it now.";
    }

    public void SetShrunk(long newBytes)
    {
        NewBytes = newBytes;
        Kind = TrackKind.Shrunk;
        Progress = 0;
        var savings = OriginalBytes > 0 ? 1 - (double)newBytes / OriginalBytes : 0;
        StatusText = savings > 0 ? "−" + Format.Percent(savings) : "SHRUNK";
        SizeText = Format.Gigabytes(newBytes);
        Detail = "Replaced after every check passed.";
        Raise(nameof(SavedBytes));
    }

    public void SetKept(double? savings, string reason)
    {
        Kind = TrackKind.Kept;
        Progress = 0;
        StatusText = savings is { } s ? "KEPT " + Format.Percent(s) : "KEPT";
        SizeText = Format.Gigabytes(OriginalBytes);
        Detail = reason;
    }

    /// <param name="at">When it failed, so the deck can name the latest failure.</param>
    public void SetFailed(string reason, DateTimeOffset? at = null)
    {
        Kind = TrackKind.Failed;
        Progress = 0;
        StatusText = FailedStatus(reason);
        SizeText = Format.Gigabytes(OriginalBytes);
        Detail = reason;
        FailedAt = at;
    }

    /// <summary>When the episode failed, for a failure seen this run or read from the journal.</summary>
    public DateTimeOffset? FailedAt { get; private set; }

    /// <summary>
    /// The status cell for a failure: <c>FAIL</c> and the check that failed, such as
    /// <c>FAIL SUBS</c>, read from the start of the first reason. The full text is the tooltip.
    /// </summary>
    public static string FailedStatus(string? reason)
    {
        var first = (reason ?? "").Split(';', 2)[0].Trim();
        var word = ReasonWords.FirstOrDefault(w => first.StartsWith(w.Prefix, StringComparison.OrdinalIgnoreCase)).Word;
        return word is null ? "FAILED" : "FAIL " + word;
    }

    private static readonly (string Prefix, string Word)[] ReasonWords =
    [
        ("Subtitle streams", "SUBS"),
        ("Audio streams", "AUDIO"),
        ("Video streams", "VIDEO"),
        ("Output video codec", "VIDEO"),
        ("Attachments", "FONTS"),
        ("Duration", "LENGTH"),
        ("Full decode", "DECODE"),
        ("File size", "SIZE"),
        ("HandBrakeCLI", "ENCODE"),
        ("ffmpeg", "FFMPEG"),
        ("ffprobe", "PROBE"),
    ];

    public void SetSkipped(string label, string reason)
    {
        Kind = TrackKind.Skipped;
        Progress = 0;
        StatusText = label;
        SizeText = Format.Gigabytes(OriginalBytes);
        Detail = reason;
    }
}
