namespace ShrinkRay.App.Services;

/// <summary>
/// Where the app keeps its files: <c>%LOCALAPPDATA%\ShrinkRay</c> unless <c>--data</c> names
/// another folder. Library paths live only in these files, never in the repo.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string dataFolder)
    {
        DataFolder = dataFolder;
    }

    public static string DefaultDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShrinkRay");

    public string DataFolder { get; }

    public string Settings => Path.Combine(DataFolder, "settings.json");

    public string Preferences => Path.Combine(DataFolder, "deck.json");

    public string Journal => Path.Combine(DataFolder, "journal.jsonl");

    public string Ledger => Path.Combine(DataFolder, "ledger.jsonl");

    /// <summary>Versioned, so a change to the eligibility rules can start a fresh cache.</summary>
    public string ProbeCache => Path.Combine(DataFolder, "probe-cache-v1.json");

    /// <summary>How fast each step has gone, so the progress readouts learn this machine.</summary>
    public string Rates => Path.Combine(DataFolder, "rates.json");

    /// <summary>The scratch folder used until the user picks one.</summary>
    public string DefaultScratch => Path.Combine(DataFolder, "Scratch");
}
