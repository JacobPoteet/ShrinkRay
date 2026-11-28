using System.ComponentModel;
using System.Windows;
using ShrinkRay.App.ViewModels;

namespace ShrinkRay.App.Views;

/// <summary>
/// Swaps the deck and the mini cassette as <see cref="MainViewModel.IsMini"/> changes. The deck
/// minimizes while the mini is up, so its taskbar button still restores it, and restoring the
/// deck by any route puts the mini away.
/// </summary>
public sealed class MiniDeckSwitch : IDisposable
{
    private readonly MainWindow _deck;
    private readonly MainViewModel _viewModel;
    private readonly Action _openDeck;
    private MiniDeckWindow? _mini;

    /// <param name="openDeck">Restores and brings the deck forward, as the tray icon does.</param>
    public MiniDeckSwitch(MainWindow deck, MainViewModel viewModel, Action openDeck)
    {
        _deck = deck;
        _viewModel = viewModel;
        _openDeck = openDeck;
        _deck.StateChanged += OnDeckStateChanged;
        _deck.Closed += OnDeckClosed;
        _viewModel.PropertyChanged += OnViewModelChanged;
    }

    public void Dispose()
    {
        _deck.StateChanged -= OnDeckStateChanged;
        _deck.Closed -= OnDeckClosed;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _mini?.Close();
        _mini = null;
    }

    private void OnDeckStateChanged(object? sender, EventArgs e) =>
        _viewModel.IsMini = _deck.WindowState == WindowState.Minimized;

    private void OnDeckClosed(object? sender, EventArgs e) => Dispose();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsMini))
        {
            return;
        }

        if (_viewModel.IsMini)
        {
            if (_deck.WindowState != WindowState.Minimized)
            {
                _deck.WindowState = WindowState.Minimized;
            }

            _mini ??= new MiniDeckWindow(_viewModel);
            if (!_mini.IsVisible)
            {
                _mini.ShowPlaced();
            }
        }
        else
        {
            _mini?.Hide();
            if (_deck.WindowState == WindowState.Minimized)
            {
                _openDeck();
            }
        }
    }
}
