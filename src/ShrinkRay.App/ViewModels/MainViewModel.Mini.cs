using System.Globalization;
using ShrinkRay.App.Mvvm;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Theme.Controls;
using ShrinkRay.Theme.Mechanics;

namespace ShrinkRay.App.ViewModels;

/// <summary>
/// The mini cassette: a small window that floats over the others while the deck is minimized.
/// It shows the tape, the run lamp, the episode percent and the time left on the whole run.
/// </summary>
public sealed partial class MainViewModel
{
    private bool _isMini;
    private RelayCommand? _showMiniCommand;
    private RelayCommand? _showDeckCommand;

    /// <summary>The mini cassette is up and the deck is minimized. The window follows this, and sets it when minimized from outside.</summary>
    public bool IsMini
    {
        get => _isMini;
        set => Set(ref _isMini, value);
    }

    /// <summary>The deck's Mini key.</summary>
    public RelayCommand ShowMiniCommand => _showMiniCommand ??= new RelayCommand(() => IsMini = true);

    /// <summary>The mini cassette's Open key.</summary>
    public RelayCommand ShowDeckCommand => _showDeckCommand ??= new RelayCommand(() => IsMini = false);

    /// <summary>Where the mini cassette was left, in screen units. Null until it has been moved.</summary>
    public (double Left, double Top)? MiniPosition =>
        _prefs.MiniLeft is { } left && _prefs.MiniTop is { } top ? (left, top) : null;

    /// <summary>Saves where the mini cassette was dragged to.</summary>
    public void MoveMini(double left, double top)
    {
        if (double.IsNaN(left) || double.IsNaN(top) || MiniPosition == (left, top))
        {
            return;
        }

        _prefs = _prefs with { MiniLeft = left, MiniTop = top };
        SavePreferences();
    }

    /// <summary>
    /// Minutes left on the whole run: the show on the tape, counted down as the deck's counter
    /// is, plus every queued show. Not capped at the deck counter's 99:59.
    /// </summary>
    public int RunLeftMinutes =>
        (_loaded is null ? 0 : ShowLeftMinutesUncapped) + (int)Math.Ceiling(QueueTime.TotalMinutes);

    private bool HasRun => _loaded is not null || HasQueue;

    /// <summary>The whole hours left on the run, two digits or more. Dashes with nothing loaded or queued.</summary>
    public string RunLeftHourDigits => HasRun
        ? (RunLeftMinutes / 60).ToString(CultureInfo.InvariantCulture).PadLeft(2)
        : "--";

    public string RunLeftMinuteDigits => HasRun
        ? (RunLeftMinutes % 60).ToString("00", CultureInfo.InvariantCulture)
        : "--";

    public string RunLeftName => HasRun
        ? $"Run time left, {Format.Duration(TimeSpan.FromMinutes(RunLeftMinutes)).ToLowerInvariant()}"
        : "Run time left, none";

    public string RunLeftDetail => !HasRun
        ? "Time left for the show and the queue"
        : HasQueue
            ? $"About {Format.Duration(TimeSpan.FromMinutes(RunLeftMinutes)).ToLowerInvariant()} for this show and {QueueCount} queued"
            : $"About {Format.Duration(TimeSpan.FromMinutes(RunLeftMinutes)).ToLowerInvariant()} for this show";

    /// <summary>The reels' turn times. They follow the encoder's speed against this machine's usual speed.</summary>
    public double SourceSecondsPerTurn => ReelMath.SecondsPerTurn(TapeDeckMath.SourceSecondsPerTurn, ReelFps, _rates.EncodeFps);

    public double OutputSecondsPerTurn => ReelMath.SecondsPerTurn(TapeDeckMath.OutputSecondsPerTurn, ReelFps, _rates.EncodeFps);

    /// <summary>The frame rate that drives the reels: only while encoding, since copying and checks have none.</summary>
    private double? ReelFps => _phase == EpisodePhase.Encoding ? _fps : null;

    /// <summary>The mini's lamp: the run lamp, in phosphor rather than red, since the mini has no Encode key.</summary>
    public LampState MiniLampState => RunLamp.State == LampState.Red ? LampState.Active : RunLamp.State;

    private void RaiseRunLeft() =>
        Raise(nameof(RunLeftMinutes), nameof(RunLeftHourDigits), nameof(RunLeftMinuteDigits), nameof(RunLeftName), nameof(RunLeftDetail));

    private void RaiseReelSpeed() =>
        Raise(nameof(SourceSecondsPerTurn), nameof(OutputSecondsPerTurn), nameof(MiniLampState));
}
