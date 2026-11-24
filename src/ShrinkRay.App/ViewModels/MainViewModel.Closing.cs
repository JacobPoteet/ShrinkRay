using ShrinkRay.App.Mvvm;
using ShrinkRay.Core.Pipeline;

namespace ShrinkRay.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool _isCloseConfirmOpen;
    private bool _isClosing;

    /// <summary>
    /// Raised when a confirmed close may go ahead because the run has ended. The window closes
    /// itself again, and <see cref="RequestClose"/> then lets it.
    /// </summary>
    public event EventHandler? CloseReady;

    /// <summary>Stop and close, or finish the swap and close.</summary>
    public RelayCommand ConfirmCloseCommand { get; private set; } = null!;

    /// <summary>Keep running. Escape presses it.</summary>
    public RelayCommand CancelCloseCommand { get; private set; } = null!;

    /// <summary>The window asked to close during a run and the deck is asking first.</summary>
    public bool IsCloseConfirmOpen
    {
        get => _isCloseConfirmOpen;
        private set
        {
            if (Set(ref _isCloseConfirmOpen, value))
            {
                RefreshClosing();
            }
        }
    }

    /// <summary>The close was confirmed. The deck waits for the run to end, then closes.</summary>
    public bool IsClosing
    {
        get => _isClosing;
        private set
        {
            if (Set(ref _isClosing, value))
            {
                RefreshClosing();
                RefreshTransport();
            }
        }
    }

    public bool IsCloseOverlayOpen => _isCloseConfirmOpen || _isClosing;

    /// <summary>
    /// The current episode is past the upload, so closing lets its swap finish instead of
    /// cancelling it. See <see cref="EpisodePhases.IsPastUpload"/>.
    /// </summary>
    public bool IsFinishingSwap => _current is not null && _phase.IsPastUpload();

    /// <summary>What closing now does to the run, for the confirm card.</summary>
    public string CloseText
    {
        get
        {
            var code = _current?.Code;
            if (_isClosing)
            {
                return IsFinishingSwap
                    ? $"Finishing the swap of {code}. ShrinkRay closes when the new file is in place."
                    : "Stopping the encode and clearing its scratch folder. ShrinkRay closes when that is done.";
            }

            if (code is null)
            {
                return "An encode run is waiting for its next episode. Closing ends the run.";
            }

            return IsFinishingSwap
                ? $"{code} is mid-swap on the share. Closing lets the swap finish first, then ends the run."
                : $"Closing stops {code} and throws its encode away. The original stays as it is, and the episode waits in the queue for the next run.";
        }
    }

    public string CloseHeading => _isClosing ? "Closing" : "Close during a run";

    /// <summary>The confirm key's label.</summary>
    public string CloseKeyLabel => IsFinishingSwap ? "Finish + close" : "Stop + close";

    /// <summary>
    /// The window's Closing event asks this first. Idle, the deck closes at once. During a run it
    /// opens the confirm card and refuses; once a confirmed close has seen the run end, it agrees.
    /// </summary>
    /// <returns>True when the window may close now.</returns>
    public bool RequestClose()
    {
        if (!IsEncoding)
        {
            return true;
        }

        if (!_isClosing)
        {
            IsCloseConfirmOpen = true;
        }

        return false;
    }

    private void CreateCloseCommands()
    {
        ConfirmCloseCommand = new RelayCommand(ConfirmClose, () => _isCloseConfirmOpen);
        CancelCloseCommand = new RelayCommand(() => IsCloseConfirmOpen = false, () => _isCloseConfirmOpen);
    }

    private void ConfirmClose()
    {
        IsCloseConfirmOpen = false;
        if (_queue is not { } queue)
        {
            // The run ended while the card was open.
            IsClosing = true;
            CloseReady?.Invoke(this, EventArgs.Empty);
            return;
        }

        IsClosing = true;
        if (IsFinishingSwap)
        {
            // Never cancel a swap that has renamed the original aside. Let it finish, start no other.
            queue.StopAfterCurrent();
        }
        else
        {
            // The same path as the Stop key: the journal records Cancelled and scratch is cleared.
            queue.Stop();
        }

        RefreshTransport();
    }

    private void RefreshClosing()
    {
        Raise(nameof(IsCloseOverlayOpen), nameof(IsFinishingSwap), nameof(CloseHeading), nameof(CloseText), nameof(CloseKeyLabel));
        ConfirmCloseCommand.Refresh();
        CancelCloseCommand.Refresh();
    }
}
