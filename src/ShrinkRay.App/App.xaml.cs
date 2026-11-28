using System.Windows;
using ShrinkRay.App.Services;
using ShrinkRay.App.Tray;
using ShrinkRay.App.ViewModels;
using ShrinkRay.App.Views;
using ShrinkRay.Core.Diagnostics;
using ShrinkRay.Core.Settings;
using ShrinkRay.Theme;

namespace ShrinkRay.App;

/// <summary>
/// ShrinkRay. Options: <c>--data &lt;folder&gt;</c> keeps settings, journal and caches somewhere
/// other than <c>%LOCALAPPDATA%\ShrinkRay</c> (for a test library), and <c>--still</c> holds
/// reels, blink and flicker. One copy runs per data folder; starting another brings the first
/// one forward.
/// </summary>
public partial class App : Application
{
    private SingleInstance? _instance;
    private TrayModel? _trayModel;
    private TrayIcon? _tray;
    private MiniDeckSwitch? _mini;

    // The tray icon goes with the app, so no ghost icon stays behind in the notification area.
    protected override void OnExit(ExitEventArgs e)
    {
        _mini?.Dispose();
        _tray?.Dispose();
        _trayModel?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    // Anything unexpected is written down before the app goes. It closes rather than carries on,
    // since the swap journal makes the next start safe and a half-working deck would not be.
    private void LogCrashes(FileLog log)
    {
        var closing = false;
        DispatcherUnhandledException += (_, e) =>
        {
            log.Write("Crash: " + e.Exception);
            e.Handled = true;
            if (closing)
            {
                return;
            }

            closing = true;
            MessageBox.Show(
                $"ShrinkRay hit an error and has to close. The next start puts any half-finished swap right.\n\nThe details are in {log.FilePath}",
                "ShrinkRay", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Write("Crash: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Write("A background task failed: " + e.Exception);
            e.SetObserved();
        };
    }

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

        // Before anything reads the data folder: recovery would act on a running copy's encode.
        _instance = SingleInstance.TryClaim(dataFolder);
        if (_instance is null)
        {
            Shutdown();
            return;
        }

        var paths = new AppPaths(dataFolder);
        var log = new FileLog(paths.Log);
        LogCrashes(log);
        log.Write($"ShrinkRay {typeof(App).Assembly.GetName().Version?.ToString(3)} started");
        var settings = SettingsStore.Load(paths.Settings);
        var preferences = DeckPreferences.Load(paths.Preferences);
        ThemeManager.Apply(preferences.Phosphor, preferences.Chassis);

        var services = Composition.Create(paths, settings, action => Dispatcher.BeginInvoke(action), log);
        var viewModel = new MainViewModel(services, settings, preferences, paths.DefaultScratch);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        _trayModel = new TrayModel(viewModel);
        _tray = new TrayIcon(window, _trayModel);
        _mini = new MiniDeckSwitch(window, viewModel, _tray.Open);
        window.Show();
        _instance.OnShowRequested(() => Dispatcher.BeginInvoke(_tray.Open));

        // Recovery runs inside StartAsync before the first scan, and Encode stays off until it ends.
        await viewModel.StartAsync();
    }
}
