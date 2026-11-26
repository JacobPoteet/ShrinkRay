using System.Collections.ObjectModel;
using ShrinkRay.App.Mvvm;
using ShrinkRay.Core.Library;

namespace ShrinkRay.App.ViewModels;

/// <summary>
/// What monitor A lists: the candidate shows, narrowed by the search and in name order or by
/// what they would save, and the size estimates behind that order.
/// </summary>
public sealed partial class MainViewModel
{
    private SizeModel _sizes = SizeModel.Empty;
    private string _search = "";

    /// <summary>The rows monitor A shows, in its order. <see cref="Shows"/> holds every candidate by name.</summary>
    public ObservableCollection<ShowRowViewModel> VisibleShows { get; } = [];

    /// <summary>Switches the list between name order and the most to save first.</summary>
    public RelayCommand ToggleSortCommand { get; private set; } = null!;

    public RelayCommand ClearSearchCommand { get; private set; } = null!;

    /// <summary>Part of a show's name; the list holds only the shows that carry it.</summary>
    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? ""))
            {
                ClearSearchCommand.Refresh();
                Arrange();
            }
        }
    }

    public bool SortBySavings => _prefs.SortBySavings;

    /// <summary>The sort key's text: the order the list is in.</summary>
    public string SortText => SortBySavings ? "Most to save" : "A to Z";

    public string SortName => SortBySavings
        ? "Listed by what they would save. List by name instead"
        : "Listed by name. List by what they would save instead";

    private void CreateLibraryCommands()
    {
        ToggleSortCommand = new RelayCommand(() =>
        {
            _prefs = _prefs with { SortBySavings = !_prefs.SortBySavings };
            SavePreferences();
            Raise(nameof(SortBySavings), nameof(SortText), nameof(SortName));
            RefreshShowDetails();
        });
        ClearSearchCommand = new RelayCommand(() => Search = "", () => _search.Length > 0);
    }

    /// <summary>Learns the size estimates again, after the ledger or the library changed.</summary>
    private void LearnSizes() => _sizes = SizeModel.From(_s.Ledger.All(), _settings.LibraryRoot);

    private double RatioFor(string? show, int cq) => _sizes.RatioFor(show, cq);

    private long SavingsFor(ShowRowViewModel row) =>
        row.Summary.ProjectedSavingsAt(RatioFor(row.FolderName, _settings.CqForShow(row.FolderName)));

    /// <summary>
    /// Brings the visible list in line with the search and the order. Rows are moved rather than
    /// rebuilt, so the cursor stays on its show.
    /// </summary>
    private void Arrange()
    {
        var wanted = Shows.Where(r => r.FolderName.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase));
        var target = (SortBySavings ? wanted.OrderByDescending(SavingsFor) : wanted).ToList();
        foreach (var gone in VisibleShows.Except(target).ToList())
        {
            VisibleShows.Remove(gone);
        }

        for (var i = 0; i < target.Count; i++)
        {
            var at = VisibleShows.IndexOf(target[i]);
            if (at < 0)
            {
                VisibleShows.Insert(i, target[i]);
            }
            else if (at != i)
            {
                VisibleShows.Move(at, i);
            }
        }

        // A search that hid the cursor's show took the cursor with it. It comes back with the show.
        if (_selectedShow is null
            && VisibleShows.FirstOrDefault(r => string.Equals(r.FolderName, ShownShow?.Name, StringComparison.OrdinalIgnoreCase)) is { } shown)
        {
            Set(ref _selectedShow, shown, nameof(SelectedShow));
        }

        Raise(nameof(MonitorMessage));
    }
}
