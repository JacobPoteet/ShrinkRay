using ShrinkRay.Core.Settings;

namespace ShrinkRay.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ShrinkRayTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Defaults_match_the_spec()
    {
        var s = new ShrinkRaySettings();

        Assert.Equal(24, s.Cq);
        Assert.Equal(0.15, s.MinimumSavings);
        Assert.True(s.ReplaceOriginals);
        Assert.True(s.RestoreFonts);
        Assert.False(s.EncodeWindowEnabled);
        Assert.Equal(new TimeOnly(23, 0), s.EncodeWindowStart);
        Assert.Equal(new TimeOnly(7, 0), s.EncodeWindowEnd);
        Assert.Equal("", s.LibraryRoot);
        Assert.Equal("", s.ScratchFolder);
    }

    [Fact]
    public void Default_path_is_under_appdata()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.Equal(Path.Combine(appData, "ShrinkRay", "settings.json"), SettingsStore.DefaultPath);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = SettingsStore.Load(SettingsPath);

        Assert.Equal(24, s.Cq);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("{ \"cq\": \"twenty\" }")]
    public void Corrupt_file_gives_defaults(string content)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, content);

        var s = SettingsStore.Load(SettingsPath);

        Assert.Equal(24, s.Cq);
        Assert.True(s.ReplaceOriginals);
    }

    [Fact]
    public void Save_and_load_round_trip()
    {
        var original = new ShrinkRaySettings
        {
            HandBrakeCliPath = @"C:\Tools\HandBrakeCLI.exe",
            LibraryRoot = @"X:\TV",
            ScratchFolder = @"D:\Scratch",
            Cq = 26,
            MinimumSavings = 0.2,
            ReplaceOriginals = false,
            RestoreFonts = false,
            EncodeWindowEnabled = true,
            EncodeWindowStart = new TimeOnly(22, 30),
            EncodeWindowEnd = new TimeOnly(6, 15),
        };

        SettingsStore.Save(SettingsPath, original);
        var loaded = SettingsStore.Load(SettingsPath);

        Assert.Equal(@"C:\Tools\HandBrakeCLI.exe", loaded.HandBrakeCliPath);
        Assert.Equal(@"X:\TV", loaded.LibraryRoot);
        Assert.Equal(@"D:\Scratch", loaded.ScratchFolder);
        Assert.Equal(26, loaded.Cq);
        Assert.Equal(0.2, loaded.MinimumSavings);
        Assert.False(loaded.ReplaceOriginals);
        Assert.False(loaded.RestoreFonts);
        Assert.True(loaded.EncodeWindowEnabled);
        Assert.Equal(new TimeOnly(22, 30), loaded.EncodeWindowStart);
        Assert.Equal(new TimeOnly(6, 15), loaded.EncodeWindowEnd);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Saved_file_uses_readable_names_and_times()
    {
        var json = SettingsStore.ToJson(new ShrinkRaySettings());

        Assert.Contains("\"handBrakeCliPath\": \"HandBrakeCLI.exe\"", json);
        Assert.Contains("\"encodeWindowStart\": \"23:00\"", json);
        Assert.Contains("\"minimumSavings\": 0.15", json);
    }

    [Fact]
    public void Nulls_and_missing_fields_take_defaults()
    {
        var s = SettingsStore.FromJson("""
            {
              "handBrakeCliPath": null,
              "ffprobePath": "   ",
              "libraryRoot": null,
              "cq": null,
              "replaceOriginals": null,
              "encodeWindowStart": null
            }
            """);

        Assert.Equal("HandBrakeCLI.exe", s.HandBrakeCliPath);
        Assert.Equal("ffprobe.exe", s.FfprobePath);
        Assert.Equal("ffmpeg.exe", s.FfmpegPath);
        Assert.Equal("", s.LibraryRoot);
        Assert.Equal(24, s.Cq);
        Assert.True(s.ReplaceOriginals);
        Assert.Equal(new TimeOnly(23, 0), s.EncodeWindowStart);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(52)]
    public void Out_of_range_cq_falls_back(int cq)
    {
        Assert.Equal(24, SettingsStore.FromJson($$"""{ "cq": {{cq}} }""").Cq);
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1")]
    [InlineData("1.5")]
    public void Out_of_range_savings_fall_back(string value)
    {
        Assert.Equal(0.15, SettingsStore.FromJson($$"""{ "minimumSavings": {{value}} }""").MinimumSavings);
    }

    [Fact]
    public void Bad_time_falls_back_and_other_fields_survive()
    {
        var s = SettingsStore.FromJson("""{ "encodeWindowEnd": "25:99", "cq": 30 }""");

        Assert.Equal(new TimeOnly(7, 0), s.EncodeWindowEnd);
        Assert.Equal(30, s.Cq);
    }

    [Fact]
    public void Normalize_fixes_nulls_set_in_code()
    {
        var s = new ShrinkRaySettings { FfmpegPath = null!, ScratchFolder = null!, Cq = 99 };

        var n = SettingsStore.Normalize(s);

        Assert.Equal("ffmpeg.exe", n.FfmpegPath);
        Assert.Equal("", n.ScratchFolder);
        Assert.Equal(24, n.Cq);
        Assert.Equal(99, s.Cq);
    }

    [Theory]
    [InlineData(false, "23:00", "07:00", "12:00", true)]
    [InlineData(true, "23:00", "07:00", "23:00", true)]
    [InlineData(true, "23:00", "07:00", "02:30", true)]
    [InlineData(true, "23:00", "07:00", "07:00", false)]
    [InlineData(true, "23:00", "07:00", "12:00", false)]
    [InlineData(true, "01:00", "05:00", "03:00", true)]
    [InlineData(true, "01:00", "05:00", "06:00", false)]
    [InlineData(true, "04:00", "04:00", "16:00", true)]
    public void Encode_window_handles_overnight_spans(bool enabled, string start, string end, string now, bool inside)
    {
        var s = new ShrinkRaySettings
        {
            EncodeWindowEnabled = enabled,
            EncodeWindowStart = TimeOnly.Parse(start),
            EncodeWindowEnd = TimeOnly.Parse(end),
        };

        Assert.Equal(inside, s.IsInsideEncodeWindow(TimeOnly.Parse(now)));
    }
}
