using ShrinkRay.App.Mvvm;

namespace ShrinkRay.App.ViewModels;

/// <summary>One quality choice in the service panel's Quality row.</summary>
public sealed class CqChipViewModel : ObservableObject
{
    private readonly Action<int> _select;
    private bool _isSelected;

    public CqChipViewModel(int index, int cq, bool isRecommended, Action<int> select)
    {
        Index = index;
        Cq = cq;
        IsRecommended = isRecommended;
        _select = select;
    }

    public int Index { get; }

    public int Cq { get; }

    /// <summary>True for the tested balance. Its label says so.</summary>
    public bool IsRecommended { get; private set; }

    /// <summary><c>28</c>, or <c>28 · REC</c> on the recommended one.</summary>
    public string Label => IsRecommended ? $"{Cq} · REC" : Cq.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string AutomationName => IsRecommended ? $"Quality {Cq}, recommended" : $"Quality {Cq}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value) && value)
            {
                _select(Index);
            }
        }
    }

    internal void Update(bool selected, bool recommended)
    {
        _isSelected = selected;
        Raise(nameof(IsSelected));
        if (IsRecommended != recommended)
        {
            IsRecommended = recommended;
            Raise(nameof(IsRecommended), nameof(Label), nameof(AutomationName));
        }
    }
}
