using ShrinkRay.App.Mvvm;
using ShrinkRay.Theme.Controls;

namespace ShrinkRay.App.ViewModels;

/// <summary>One season key above the J-card.</summary>
public sealed class SeasonChipViewModel : ObservableObject
{
    private readonly Action<int> _select;
    private bool _isSelected;
    private LampState _lamp;
    private bool _blink;

    public SeasonChipViewModel(int number, Action<int> select)
    {
        Number = number;
        _select = select;
    }

    public int Number { get; }

    /// <summary><c>S1</c>, or <c>SP</c> for specials.</summary>
    public string Label => Number == 0 ? "SP" : "S" + Number;

    public string AutomationName => Number == 0 ? "Specials" : "Season " + Number;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value) && value)
            {
                _select(Number);
            }
        }
    }

    /// <summary>Active and blinking while one of its episodes encodes; Ok when nothing is left to do.</summary>
    public LampState Lamp
    {
        get => _lamp;
        set => Set(ref _lamp, value);
    }

    public bool Blink
    {
        get => _blink;
        set => Set(ref _blink, value);
    }

    /// <summary>Marks the chip selected without calling back, for when the deck picks the season.</summary>
    internal void SelectQuietly(bool selected)
    {
        _isSelected = selected;
        Raise(nameof(IsSelected));
    }
}
