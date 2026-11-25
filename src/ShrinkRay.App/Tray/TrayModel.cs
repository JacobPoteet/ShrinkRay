using System.ComponentModel;
using ShrinkRay.App.ViewModels;

namespace ShrinkRay.App.Tray;

/// <summary>
/// What the tray icon shows: its state and tooltip, worked out from the deck's run-state
/// properties. No window or NotifyIcon in here, so tests cover it. <see cref="TrayIcon"/> only
/// paints what this says.
/// </summary>
public sealed class TrayModel : IDisposable
{
    /// <summary>A NotifyIcon tooltip holds 127 characters.</summary>
    public const int MaxTooltipLength = 127;

    private const string AppName = "ShrinkRay";

    private static readonly HashSet<string> Watched =
    [
        nameof(MainViewModel.IsEncoding),
        nameof(MainViewModel.IsPaused),
        nameof(MainViewModel.IsStopArmed),
        nameof(MainViewModel.IsClosing),
        nameof(MainViewModel.EpisodeDigits),
        nameof(MainViewModel.PercentDigits),
        nameof(MainViewModel.LastFailureDetail),
    ];

    private readonly MainViewModel _viewModel;

    public TrayModel(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnPropertyChanged;
        Refresh();
    }

    /// <summary>Raised when <see cref="State"/> or <see cref="Tooltip"/> changed.</summary>
    public event EventHandler? Changed;

    public TrayState State { get; private set; }

    public string Tooltip { get; private set; } = AppName;

    /// <summary>The menu item that arms the stop after this episode, or takes it back while armed.</summary>
    public string StopAfterText { get; private set; } = StopAfterTextFor(armed: false);

    /// <summary>The stop item works during a run that is not already closing the window.</summary>
    public bool CanStopAfter { get; private set; }

    public static string StopAfterTextFor(bool armed) => armed ? "Keep going" : "Stop after this episode";

    /// <summary>Arms the stop, or takes it back when armed. The deck decides what that means.</summary>
    public void ToggleStopAfter() => _viewModel.ToggleStopAfterCommand.Execute(null);

    public static TrayState StateFor(bool isEncoding, bool isPaused, bool hasFailure) =>
        isEncoding && isPaused ? TrayState.Paused
        : isEncoding ? TrayState.Encoding
        : hasFailure ? TrayState.Failed
        : TrayState.Idle;

    /// <param name="episode">The episode's code, such as S01E04. Empty between episodes.</param>
    /// <param name="percent">The progress number, such as 42.3. Empty when unknown.</param>
    /// <param name="failure">The latest failure in full. Empty when nothing failed.</param>
    public static string TooltipFor(TrayState state, string episode, string percent, string failure)
    {
        episode = episode.Trim();
        percent = percent.Trim();
        var progress = string.Join(" ", new[] { episode, percent.Length > 0 ? percent + "%" : "" }.Where(s => s.Length > 0));
        var text = state switch
        {
            TrayState.Encoding => progress.Length > 0 ? $"{AppName}: {progress}" : $"{AppName}: encoding",
            TrayState.Paused => progress.Length > 0 ? $"{AppName}: paused at {progress}" : $"{AppName}: paused",
            TrayState.Failed => failure.Length > 0 ? $"{AppName}: {failure}" : $"{AppName}: last episode failed",
            _ => AppName,
        };

        return text.Length <= MaxTooltipLength ? text : text[..(MaxTooltipLength - 3)] + "...";
    }

    public void Dispose() => _viewModel.PropertyChanged -= OnPropertyChanged;

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || Watched.Contains(e.PropertyName))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var failure = _viewModel.LastFailureDetail;
        var state = StateFor(_viewModel.IsEncoding, _viewModel.IsPaused, failure.Length > 0);
        var episode = _viewModel.IsEncoding ? _viewModel.EpisodeDigits.Trim('-', ' ') : "";
        var percent = _viewModel.IsEncoding ? _viewModel.PercentDigits : "";
        var tooltip = TooltipFor(state, episode, percent, failure);
        var stopAfter = StopAfterTextFor(_viewModel.IsStopArmed);
        var canStopAfter = _viewModel.ToggleStopAfterCommand.CanExecute(null);
        if (state == State && tooltip == Tooltip && stopAfter == StopAfterText && canStopAfter == CanStopAfter)
        {
            return;
        }

        State = state;
        Tooltip = tooltip;
        StopAfterText = stopAfter;
        CanStopAfter = canStopAfter;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
