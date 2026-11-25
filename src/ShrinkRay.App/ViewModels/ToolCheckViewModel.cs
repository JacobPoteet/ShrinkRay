using ShrinkRay.App.Mvvm;
using ShrinkRay.Theme.Controls;

namespace ShrinkRay.App.ViewModels;

/// <summary>One lamp of setup's self-test, and what to do when the thing it watches is missing.</summary>
public sealed class ToolCheckViewModel : ObservableObject
{
    private bool _isMissing;
    private string _copyLabel = "Copy";

    /// <param name="fileName">The program's file name, for Locate. Null when there is no file to pick.</param>
    /// <param name="command">The install command to show. Null when a command cannot fix it.</param>
    /// <param name="note">What to do when there is no command.</param>
    public ToolCheckViewModel(
        string name, string? fileName, string? command, string note, Action<ToolCheckViewModel> copy, Action<ToolCheckViewModel> locate)
    {
        Name = name;
        FileName = fileName;
        Command = command ?? "";
        Note = note;
        Lamp = new LampViewModel(name);
        CopyCommand = new RelayCommand(() => copy(this), () => HasCommand);
        LocateCommand = new RelayCommand(() => locate(this), () => CanLocate);
    }

    public string Name { get; }

    public string? FileName { get; }

    public string Command { get; }

    public string Note { get; }

    public bool HasCommand => Command.Length > 0;

    public bool CanLocate => FileName is not null;

    /// <summary>The lamp and its word: the name while untested, then what was found.</summary>
    public LampViewModel Lamp { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand LocateCommand { get; }

    /// <summary>The self-test looked and found nothing.</summary>
    public bool IsMissing
    {
        get => _isMissing;
        private set => Set(ref _isMissing, value);
    }

    public string CopyLabel
    {
        get => _copyLabel;
        private set => Set(ref _copyLabel, value);
    }

    /// <summary>The Locate key's name for screen readers.</summary>
    public string LocateName => $"Locate {FileName}";

    public string CopyName => $"Copy the install command for {Name}";

    public void Reset()
    {
        IsMissing = false;
        CopyLabel = "Copy";
        Lamp.Show(Name, LampState.Off);
    }

    /// <param name="detail">What was found, such as the card's name. Null shows the name alone.</param>
    public void Found(string? detail = null)
    {
        IsMissing = false;
        Lamp.Show(detail is null ? Name : $"{Name} · {detail}", LampState.Ok);
    }

    public void Missing()
    {
        IsMissing = true;
        Lamp.Show(Name + " missing", LampState.Active, blink: true);
    }

    public void MarkCopied() => CopyLabel = "Copied";
}
