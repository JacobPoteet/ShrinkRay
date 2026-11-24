using ShrinkRay.App.Services;
using ShrinkRay.App.ViewModels;
using ShrinkRay.Theme;

namespace ShrinkRay.App.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(856, "14:16")]
    [InlineData(5999, "99:59")]
    [InlineData(6000, "1:40")]
    public void Tape_time_fits_five_digits(double seconds, string expected)
    {
        Assert.Equal(expected, Format.TapeTime(seconds));
    }

    [Theory]
    [InlineData(291_400_000_000, "291")]
    [InlineData(4_240_000_000, "4.2")]
    [InlineData(9_960_000_000, "10")]
    public void Whole_gigabytes_keep_one_decimal_for_small_shows(long bytes, string expected)
    {
        Assert.Equal(expected, Format.WholeGigabytes(bytes));
    }

    [Theory]
    [InlineData(150, "3M")]
    [InlineData(16_320, "4H 32M")]
    public void Durations_read_in_hours_and_minutes(int seconds, string expected)
    {
        Assert.Equal(expected, Format.Duration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Deck_preferences_survive_a_save_and_load()
    {
        var file = Path.Combine(Path.GetTempPath(), "shrinkray-test-" + Guid.NewGuid().ToString("N"), "deck.json");
        try
        {
            new DeckPreferences { Phosphor = Phosphor.Ice, Chassis = Chassis.Graphite, LastShow = "Scrubs" }.Save(file);

            var loaded = DeckPreferences.Load(file);

            Assert.Equal(Phosphor.Ice, loaded.Phosphor);
            Assert.Equal(Chassis.Graphite, loaded.Chassis);
            Assert.Equal("Scrubs", loaded.LastShow);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }

    [Fact]
    public void Corrupt_preferences_load_as_defaults()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "{ nope");

            Assert.Equal(new DeckPreferences(), DeckPreferences.Load(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 2070 SUPER", "RTX 2070S")]
    [InlineData("NVIDIA GeForce RTX 4090", "RTX 4090")]
    public void Gpu_names_shorten_like_the_silkscreen(string name, string expected)
    {
        Assert.Equal(expected, NvidiaSmiMonitor.ShortName(name));
    }
}
