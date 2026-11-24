using System.Windows;
using ShrinkRay.App.Services;
using ShrinkRay.App.ViewModels;
using ShrinkRay.App.Views;
using ShrinkRay.Core.Settings;
using ShrinkRay.Theme;

namespace ShrinkRay.App;

/// <summary>
/// ShrinkRay. Options: <c>--data &lt;folder&gt;</c> keeps settings, journal and caches somewhere
/// other than <c>%LOCALAPPDATA%\ShrinkRay</c> (for a test library), and <c>--still</c> holds
/// reels, blink and flicker.
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataFolder = AppPaths.DefaultDataFolder;
        for (var i = 0; i < e.Args.Length; i++)
        {
            switch (e.Args[i].ToLowerInvariant())
            {
                case "--data" when i + 1 < e.Args.Length:
                    dataFolder = e.Args[++i];
                    break;
                case "--still":
                    MotionPolicy.Override = false;
                    break;
            }
        }

        var paths = new AppPaths(dataFolder);
        var settings = SettingsStore.Load(paths.Settings);
        var preferences = DeckPreferences.Load(paths.Preferences);
        ThemeManager.Apply(preferences.Phosphor, preferences.Chassis);

        var services = Composition.Create(paths, settings, action => Dispatcher.BeginInvoke(action));
        var viewModel = new MainViewModel(services, settings, preferences, paths.DefaultScratch);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();

        // Recovery runs inside StartAsync before the first scan, and Encode stays off until it ends.
        await viewModel.StartAsync();
    }
}
