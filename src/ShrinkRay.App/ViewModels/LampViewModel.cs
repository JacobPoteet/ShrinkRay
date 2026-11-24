using ShrinkRay.App.Mvvm;
using ShrinkRay.Theme.Controls;

namespace ShrinkRay.App.ViewModels;

/// <summary>A status lamp and the word printed beside it.</summary>
public sealed class LampViewModel : ObservableObject
{
    private string _label;
    private LampState _state;
    private bool _blink;

    public LampViewModel(string label, LampState state = LampState.Off, bool blink = false)
    {
        _label = label;
        _state = state;
        _blink = blink;
    }

    public string Label
    {
        get => _label;
        set => Set(ref _label, value);
    }

    public LampState State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    public bool Blink
    {
        get => _blink;
        set => Set(ref _blink, value);
    }

    public void Show(string label, LampState state, bool blink = false)
    {
        Label = label;
        State = state;
        Blink = blink;
    }
}
