using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ShrinkRay.App.ViewModels;

namespace ShrinkRay.App.Views;

/// <summary>The main deck. Everything it shows comes from <see cref="MainViewModel"/>.</summary>
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;
        _timer.Tick += (_, _) => viewModel.Tick();
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
        StateChanged += (_, _) => FitMaximized();
        Drop += OnDrop;
        DragOver += OnDragOver;
    }

    public MainViewModel ViewModel { get; }

    private static string? DroppedFolder(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths && System.IO.Directory.Exists(paths[0]) ? paths[0] : null;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFolder(e) is not null && !ViewModel.IsEncoding ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DroppedFolder(e) is { } folder)
        {
            ViewModel.DropFolder(folder);
        }
    }

    // A borderless window covers the screen edge when maximized; pad it back inside the work area.
    private void FitMaximized() =>
        Chassis.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // The latch keys show the view model's state, not their own toggle.
    private void OnLatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton key)
        {
            key.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
        }
    }

    private void OnShowDoubleClick(object sender, MouseButtonEventArgs e) => ViewModel.LoadTapeCommand.Execute(null);

    private void OnShowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.LoadTapeCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnScrimDown(object sender, MouseButtonEventArgs e) => ViewModel.CloseSettingsCommand.Execute(null);
}
