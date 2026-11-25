using ShrinkRay.App.Mvvm;
using ShrinkRay.Core.Pipeline;
using ShrinkRay.Theme.Controls;

namespace ShrinkRay.App.ViewModels;

/// <summary>
/// The Stop key. Pressed mid-episode it opens a card: let the episode finish, or stop now and
/// throw its encode away. Once the finish is armed the key stays latched and the card offers
/// Keep going instead. A swap in progress always finishes, whatever is chosen.
/// </summary>
public sealed partial class MainViewModel
{
    private bool _isStopArmed;
    private bool _isStopCardOpen;

    /// <summary>The card's first key: arm the stop after this episode, or take it back when armed. Enter presses it.</summary>
    public RelayCommand StopPrimaryCommand { get; private set; } = null!;

    /// <summary>The card's second key, and today's Stop path: the journal records Cancelled and scratch is cleared.</summary>
    public RelayCommand StopNowCommand { get; private set; } = null!;

    /// <summary>Escape: closes whichever card is open and changes nothing.</summary>
    public RelayCommand DismissCardCommand { get; private set; } = null!;

    /// <summary>The tray's item: arms the stop after this episode, or takes it back when armed.</summary>
    public RelayCommand ToggleStopAfterCommand { get; private set; } = null!;

    /// <summary>The run ends when the current episode does. The Stop key stays latched while this is true.</summary>
    public bool IsStopArmed
    {
        get => _isStopArmed;
        private set
        {
            if (Set(ref _isStopArmed, value))
            {
                RefreshStopping();
                RefreshTransport();
            }
        }
    }

    /// <summary>Stop was pressed mid-episode and the deck is asking what to do.</summary>
    public bool IsStopCardOpen
    {
        get => _isStopCardOpen;
        private set
        {
            if (Set(ref _isStopCardOpen, value))
            {
                RefreshStopping();
            }
        }
    }

    public LampState StopLamp => _isStopArmed ? LampState.Active : LampState.Off;

    public bool StopLampBlink => _isStopArmed;

    public string StopKeyName => _isStopArmed ? "Stopping after this episode" : "Stop the encode";

    public string StopCardHeading => _isStopArmed ? "Last episode" : "Stop";

    public string StopCardText
    {
        get
        {
            var code = _current?.Code ?? "This episode";
            if (IsFinishingSwap)
            {
                return $"{code} is mid-swap on the share. It finishes first, then the run ends.";
            }

            return _isStopArmed
                ? $"The run ends after {code}. Stop now throws its encode away."
                : $"{code} is part way through. Let it finish, or stop now and throw its encode away.";
        }
    }

    /// <summary>The first key's label.</summary>
    public string StopPrimaryLabel => _isStopArmed ? "Keep going" : "After this episode";

    /// <summary>The stop is armed, so there is a request to take back.</summary>
    public string StopPrimaryName => _isStopArmed
        ? "Keep going and run the rest of the list"
        : "Stop after this episode finishes";

    /// <summary>
    /// Nothing to finish: between episodes, waiting for the encode window, or held before an
    /// episode starts. Stop acts at once.
    /// </summary>
    private bool NothingToFinish => _queue is { } queue && (_current is null || queue.State == QueueState.WaitingForWindow);

    private void CreateStopCommands()
    {
        StopPrimaryCommand = new RelayCommand(StopPrimary, () => _isStopCardOpen);
        StopNowCommand = new RelayCommand(StopNow, () => _isStopCardOpen);
        DismissCardCommand = new RelayCommand(Dismiss, () => _isStopCardOpen || _isCloseConfirmOpen);
        ToggleStopAfterCommand = new RelayCommand(ToggleStopAfter, () => IsEncoding && !_isClosing);
    }

    private void Stop()
    {
        if (_queue is null)
        {
            RefreshTransport();
            return;
        }

        if (NothingToFinish)
        {
            StopNow();
        }
        else if (IsFinishingSwap)
        {
            // Never cancel a swap that has renamed the original aside; there is nothing to ask.
            ArmStop();
        }
        else
        {
            IsStopCardOpen = true;
        }
    }

    private void StopPrimary()
    {
        if (_isStopArmed)
        {
            KeepGoing();
        }
        else
        {
            ArmStop();
        }
    }

    /// <summary>The Stop path: the episode is cancelled, the journal records it and scratch is cleared.</summary>
    private void StopNow()
    {
        IsStopCardOpen = false;
        if (IsFinishingSwap)
        {
            ArmStop();
            return;
        }

        _queue?.Stop();
        RefreshTransport();
    }

    /// <summary>Lets the current episode finish, then ends the run.</summary>
    private void ArmStop()
    {
        IsStopCardOpen = false;
        if (_queue is not { } queue)
        {
            return;
        }

        // Set before the queue hears it: the run may end at once, and its end clears the flag.
        IsStopArmed = true;
        queue.StopAfterCurrent();
    }

    private void KeepGoing()
    {
        IsStopCardOpen = false;
        if (_queue is { } queue && queue.ContinueAfterCurrent())
        {
            IsStopArmed = false;
        }
    }

    private void ToggleStopAfter()
    {
        if (_isStopArmed)
        {
            KeepGoing();
        }
        else
        {
            ArmStop();
        }
    }

    private void Dismiss()
    {
        if (_isStopCardOpen)
        {
            IsStopCardOpen = false;
        }
        else
        {
            IsCloseConfirmOpen = false;
        }
    }

    /// <summary>The run ended, so any stop request and its card are spent.</summary>
    private void ResetStopping()
    {
        _isStopCardOpen = false;
        IsStopArmed = false;
        RefreshStopping();
    }

    private void RefreshStopping()
    {
        Raise(nameof(IsStopCardOpen), nameof(StopLamp), nameof(StopLampBlink), nameof(StopKeyName), nameof(StopCardHeading),
            nameof(StopCardText), nameof(StopPrimaryLabel), nameof(StopPrimaryName), nameof(CloseText));
        StopPrimaryCommand.Refresh();
        StopNowCommand.Refresh();
        DismissCardCommand.Refresh();
        ToggleStopAfterCommand.Refresh();
    }
}
