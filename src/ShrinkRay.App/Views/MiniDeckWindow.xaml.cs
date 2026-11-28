using System.Windows;
using System.Windows.Input;
using ShrinkRay.App.Controls;
using ShrinkRay.App.ViewModels;

namespace ShrinkRay.App.Views;

/// <summary>
/// The mini cassette. Everything it shows comes from <see cref="MainViewModel"/>; this handles
/// the drag, the double-click back to the deck, and where the window opens.
/// </summary>
public partial class MiniDeckWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MiniDeckWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
    }

    /// <summary>Shows the mini cassette where it was last left, or above the tray the first time.</summary>
    public void ShowPlaced()
    {
        // Measure first, so the window knows its size before it picks a spot.
        Case.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Case.DesiredSize;
        var screens = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var place = MiniPlacement.Place(_viewModel.MiniPosition, size, SystemParameters.WorkArea, screens);
        Left = place.X;
        Top = place.Y;
        Show();
    }

    private void OnCaseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _viewModel.ShowDeckCommand.Execute(null);
            return;
        }

        DragMove();
        _viewModel.MoveMini(Left, Top);
    }
}
