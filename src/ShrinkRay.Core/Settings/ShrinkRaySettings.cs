namespace ShrinkRay.Core.Settings;

/// <summary>User settings, stored as JSON by <see cref="SettingsStore"/>.</summary>
public sealed class ShrinkRaySettings
{
    public const int DefaultCq = 28;
    public const double DefaultMinimumSavings = 0.15;
    public const string DefaultHandBrakeCliPath = "HandBrakeCLI.exe";
    public const string DefaultFfprobePath = "ffprobe.exe";
    public const string DefaultFfmpegPath = "ffmpeg.exe";
    public static readonly TimeOnly DefaultEncodeWindowStart = new(23, 0);
    public static readonly TimeOnly DefaultEncodeWindowEnd = new(7, 0);

    /// <summary>A bare file name is looked up on PATH.</summary>
    public string HandBrakeCliPath { get; set; } = DefaultHandBrakeCliPath;

    public string FfprobePath { get; set; } = DefaultFfprobePath;
    public string FfmpegPath { get; set; } = DefaultFfmpegPath;

    /// <summary>The TV library folder. Empty until the user picks one.</summary>
    public string LibraryRoot { get; set; } = "";

    /// <summary>A local folder for copies, encodes and checks. Empty until the user picks one.</summary>
    public string ScratchFolder { get; set; } = "";

    /// <summary>NVENC constant quality for every show without an override. Lower means higher quality.</summary>
    public int Cq { get; set; } = DefaultCq;

    /// <summary>
    /// Per-show CQ, keyed by the show's folder name (such as <c>Example Show</c>), ignoring case.
    /// A show listed here encodes at its own CQ instead of <see cref="Cq"/>.
    /// </summary>
    public Dictionary<string, int> ShowCqOverrides { get; set; } = NewOverrides();

    /// <summary>The fraction of the original size an encode must save to replace it.</summary>
    public double MinimumSavings { get; set; } = DefaultMinimumSavings;

    public bool ReplaceOriginals { get; set; } = true;
    public bool RestoreFonts { get; set; } = true;

    /// <summary>Copy FLAC, TrueHD, DTS-HD MA, ALAC and PCM tracks as they are, rather than to AAC.</summary>
    public bool KeepLosslessAudio { get; set; }

    public bool EncodeWindowEnabled { get; set; }
    public TimeOnly EncodeWindowStart { get; set; } = DefaultEncodeWindowStart;
    public TimeOnly EncodeWindowEnd { get; set; } = DefaultEncodeWindowEnd;

    /// <summary>
    /// False from the moment first-run setup opens until the first-episode card is answered,
    /// then true. Null in settings that setup never touched.
    /// </summary>
    public bool? FirstRunCompleted { get; set; }

    /// <summary>Setup's cards are done and the next run is the trial: it stops after one episode.</summary>
    public bool FirstEpisodePending { get; set; }

    public ShrinkRaySettings Clone()
    {
        var copy = (ShrinkRaySettings)MemberwiseClone();
        copy.ShowCqOverrides = NewOverrides(ShowCqOverrides);
        return copy;
    }

    /// <summary>The CQ for a show: its override when one exists, otherwise <see cref="Cq"/>.</summary>
    /// <param name="showFolderName">The show's folder name, or null when unknown.</param>
    public int CqForShow(string? showFolderName) =>
        !string.IsNullOrWhiteSpace(showFolderName)
        && ShowCqOverrides is not null
        && ShowCqOverrides.TryGetValue(showFolderName.Trim(), out var cq)
            ? cq
            : Cq;

    /// <summary>An override map that ignores case in show names.</summary>
    public static Dictionary<string, int> NewOverrides(IEnumerable<KeyValuePair<string, int>>? entries = null)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (show, cq) in entries ?? [])
        {
            map[show] = cq;
        }

        return map;
    }

    /// <summary>
    /// True when an encode may start at this time of day. Always true with the window off.
    /// A window whose start is after its end runs overnight; equal start and end means all day.
    /// </summary>
    public bool IsInsideEncodeWindow(TimeOnly now)
    {
        if (!EncodeWindowEnabled || EncodeWindowStart == EncodeWindowEnd)
        {
            return true;
        }

        return EncodeWindowStart < EncodeWindowEnd
            ? now >= EncodeWindowStart && now < EncodeWindowEnd
            : now >= EncodeWindowStart || now < EncodeWindowEnd;
    }
}
