using System.Text.Json;
using System.Text.Json.Serialization;
using ShrinkRay.Theme;

namespace ShrinkRay.App.Services;

/// <summary>How the deck looks and which tape was in it. Engine settings live in <c>settings.json</c>.</summary>
public sealed record DeckPreferences
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public Phosphor Phosphor { get; init; } = Phosphor.Amber;

    public Chassis Chassis { get; init; } = Chassis.Beige;

    /// <summary>The show folder name last loaded, reloaded on the next start.</summary>
    public string? LastShow { get; init; }

    /// <summary>The shows waiting to run after the one on the tape, by folder name, next first.</summary>
    public IReadOnlyList<string>? Queue { get; init; }

    /// <summary>The library lists the most to save first, rather than by name.</summary>
    public bool SortBySavings { get; init; }

    /// <summary>Where the mini cassette was last left, in screen units. Unset puts it above the tray.</summary>
    public double? MiniLeft { get; init; }

    public double? MiniTop { get; init; }

    public static DeckPreferences Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<DeckPreferences>(File.ReadAllText(path), Options) ?? new DeckPreferences()
                : new DeckPreferences();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new DeckPreferences();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
        File.Move(temp, path, overwrite: true);
    }
}
