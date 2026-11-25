using ShrinkRay.App.Mvvm;
using ShrinkRay.Core.Library;

namespace ShrinkRay.App.ViewModels;

/// <summary>One show on monitor A.</summary>
public sealed class ShowRowViewModel : ObservableObject
{
    private ShowSummary _summary;
    private ShowScan _scan;
    private bool _isLoaded;
    private string _detail = "";

    public ShowRowViewModel(ShowScan scan, ShowSummary summary)
    {
        _scan = scan;
        _summary = summary;
    }

    public ShowScan Scan => _scan;

    public ShowSummary Summary => _summary;

    public string FolderName => _scan.Name;

    public string Name => _scan.Name.ToUpperInvariant();

    /// <summary>Whole gigabytes. No unit: the pixel font's G reads as a 6, and the tooltip and automation name say gigabytes.</summary>
    public string SizeText => Format.WholeGigabytes(_summary.TotalBytes);

    /// <summary>Seasons, episodes, what is left to do and the estimated saving. Shown as the row's tooltip.</summary>
    public string Detail
    {
        get => _detail;
        set
        {
            if (Set(ref _detail, value))
            {
                Raise(nameof(AutomationName));
            }
        }
    }

    public string AutomationName => _detail.Length > 0
        ? $"{_scan.Name}, {SizeText} gigabytes. {_detail.Replace("\n", " ")}"
        : $"{_scan.Name}, {SizeText} gigabytes";

    public bool IsLoaded
    {
        get => _isLoaded;
        set
        {
            if (Set(ref _isLoaded, value))
            {
                Raise(nameof(Mark));
            }
        }
    }

    /// <summary>A star marks the show in the deck.</summary>
    public string Mark => _isLoaded ? "*" : "";

    public void Update(ShowScan scan, ShowSummary summary)
    {
        _scan = scan;
        _summary = summary;
        Raise(nameof(Scan), nameof(Summary), nameof(Name), nameof(SizeText), nameof(AutomationName));
    }
}
