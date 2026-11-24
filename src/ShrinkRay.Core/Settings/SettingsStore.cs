using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShrinkRay.Core.Settings;

/// <summary>
/// Reads and writes <c>%APPDATA%\ShrinkRay\settings.json</c>. A missing, unreadable or corrupt
/// file gives the defaults, and missing or null fields take their default values.
/// </summary>
public static class SettingsStore
{
    private const string TimeFormat = "HH:mm";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShrinkRay", "settings.json");

    public static ShrinkRaySettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new ShrinkRaySettings();
            }

            return FromJson(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ShrinkRaySettings();
        }
    }

    /// <summary>Writes to a temp file and renames it over the old one, so a crash never leaves half a file.</summary>
    public static void Save(string path, ShrinkRaySettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, ToJson(settings));
        File.Move(temp, path, overwrite: true);
    }

    public static ShrinkRaySettings FromJson(string json)
    {
        SettingsFile? file;
        try
        {
            file = JsonSerializer.Deserialize<SettingsFile>(json, Options);
        }
        catch (JsonException)
        {
            return new ShrinkRaySettings();
        }

        if (file is null)
        {
            return new ShrinkRaySettings();
        }

        var settings = new ShrinkRaySettings
        {
            HandBrakeCliPath = PathOr(file.HandBrakeCliPath, ShrinkRaySettings.DefaultHandBrakeCliPath),
            FfprobePath = PathOr(file.FfprobePath, ShrinkRaySettings.DefaultFfprobePath),
            FfmpegPath = PathOr(file.FfmpegPath, ShrinkRaySettings.DefaultFfmpegPath),
            LibraryRoot = file.LibraryRoot?.Trim() ?? "",
            ScratchFolder = file.ScratchFolder?.Trim() ?? "",
            Cq = file.Cq ?? ShrinkRaySettings.DefaultCq,
            ShowCqOverrides = ReadOverrides(file.ShowCqOverrides),
            MinimumSavings = file.MinimumSavings ?? ShrinkRaySettings.DefaultMinimumSavings,
            ReplaceOriginals = file.ReplaceOriginals ?? true,
            RestoreFonts = file.RestoreFonts ?? true,
            EncodeWindowEnabled = file.EncodeWindowEnabled ?? false,
            EncodeWindowStart = TimeOr(file.EncodeWindowStart, ShrinkRaySettings.DefaultEncodeWindowStart),
            EncodeWindowEnd = TimeOr(file.EncodeWindowEnd, ShrinkRaySettings.DefaultEncodeWindowEnd),
        };
        return Normalize(settings);
    }

    public static string ToJson(ShrinkRaySettings settings)
    {
        var s = Normalize(settings);
        var file = new SettingsFile
        {
            HandBrakeCliPath = s.HandBrakeCliPath,
            FfprobePath = s.FfprobePath,
            FfmpegPath = s.FfmpegPath,
            LibraryRoot = s.LibraryRoot,
            ScratchFolder = s.ScratchFolder,
            Cq = s.Cq,
            ShowCqOverrides = s.ShowCqOverrides
                .OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(o => o.Key, o => (int?)o.Value),
            MinimumSavings = s.MinimumSavings,
            ReplaceOriginals = s.ReplaceOriginals,
            RestoreFonts = s.RestoreFonts,
            EncodeWindowEnabled = s.EncodeWindowEnabled,
            EncodeWindowStart = s.EncodeWindowStart.ToString(TimeFormat, CultureInfo.InvariantCulture),
            EncodeWindowEnd = s.EncodeWindowEnd.ToString(TimeFormat, CultureInfo.InvariantCulture),
        };
        return JsonSerializer.Serialize(file, Options);
    }

    /// <summary>Replaces nulls and out-of-range values with defaults. Returns a new object.</summary>
    public static ShrinkRaySettings Normalize(ShrinkRaySettings settings)
    {
        var s = settings.Clone();
        s.HandBrakeCliPath = PathOr(s.HandBrakeCliPath, ShrinkRaySettings.DefaultHandBrakeCliPath);
        s.FfprobePath = PathOr(s.FfprobePath, ShrinkRaySettings.DefaultFfprobePath);
        s.FfmpegPath = PathOr(s.FfmpegPath, ShrinkRaySettings.DefaultFfmpegPath);
        s.LibraryRoot = s.LibraryRoot?.Trim() ?? "";
        s.ScratchFolder = s.ScratchFolder?.Trim() ?? "";

        // NVENC's constant quality scale runs 0 to 51; 0 means lossless and is never wanted here.
        if (!IsValidCq(s.Cq))
        {
            s.Cq = ShrinkRaySettings.DefaultCq;
        }

        // Drop blank show names and CQ values out of range, so a bad entry falls back to the default.
        s.ShowCqOverrides = ShrinkRaySettings.NewOverrides(
            s.ShowCqOverrides
                .Where(o => !string.IsNullOrWhiteSpace(o.Key) && IsValidCq(o.Value))
                .Select(o => KeyValuePair.Create(o.Key.Trim(), o.Value)));

        if (!double.IsFinite(s.MinimumSavings) || s.MinimumSavings < 0 || s.MinimumSavings >= 1)
        {
            s.MinimumSavings = ShrinkRaySettings.DefaultMinimumSavings;
        }

        return s;
    }

    private static bool IsValidCq(int cq) => cq is >= 1 and <= 51;

    private static Dictionary<string, int> ReadOverrides(Dictionary<string, int?>? entries) =>
        ShrinkRaySettings.NewOverrides(
            (entries ?? [])
                .Where(o => o.Value is not null)
                .Select(o => KeyValuePair.Create(o.Key, o.Value!.Value)));

    private static string PathOr(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static TimeOnly TimeOr(string? value, TimeOnly fallback) =>
        TimeOnly.TryParseExact(value?.Trim(), [TimeFormat, "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : fallback;

    /// <summary>The file shape: every field nullable so a missing or null value falls back to its default.</summary>
    private sealed class SettingsFile
    {
        public string? HandBrakeCliPath { get; set; }
        public string? FfprobePath { get; set; }
        public string? FfmpegPath { get; set; }
        public string? LibraryRoot { get; set; }
        public string? ScratchFolder { get; set; }
        public int? Cq { get; set; }
        public Dictionary<string, int?>? ShowCqOverrides { get; set; }
        public double? MinimumSavings { get; set; }
        public bool? ReplaceOriginals { get; set; }
        public bool? RestoreFonts { get; set; }
        public bool? EncodeWindowEnabled { get; set; }
        public string? EncodeWindowStart { get; set; }
        public string? EncodeWindowEnd { get; set; }
    }
}
