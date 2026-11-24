namespace ShrinkRay.Core.Settings;

/// <summary>User settings, stored as JSON in the user's application data folder.</summary>
public sealed class ShrinkRaySettings
{
    public const int DefaultCq = 24;
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

    /// <summary>NVENC constant quality. Provisional until the CQ test settles it.</summary>
    public int Cq { get; set; } = DefaultCq;

    /// <summary>The fraction of the original size an encode must save to replace it.</summary>
    public double MinimumSavings { get; set; } = DefaultMinimumSavings;

    public bool ReplaceOriginals { get; set; } = true;
    public bool RestoreFonts { get; set; } = true;

    public bool EncodeWindowEnabled { get; set; }
    public TimeOnly EncodeWindowStart { get; set; } = DefaultEncodeWindowStart;
    public TimeOnly EncodeWindowEnd { get; set; } = DefaultEncodeWindowEnd;

    public ShrinkRaySettings Clone() => (ShrinkRaySettings)MemberwiseClone();
}
