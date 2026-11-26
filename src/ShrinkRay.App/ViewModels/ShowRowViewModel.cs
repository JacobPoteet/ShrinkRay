using System.Globalization;
using System.Windows.Input;
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
    private int? _queuePosition;
    private long? _savings;

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

    /// <summary>The row's number: the show's size, or what it would save while the list is in that order.</summary>
    public string ColumnText => _savings is { } bytes ? Format.WholeGigabytes(bytes) : SizeText;

    /// <summary>Set while the list is in savings order, so the number shown is the one sorted by.</summary>
    public long? ShownSavings
    {
        get => _savings;
        set
        {
            if (Set(ref _savings, value))
            {
                Raise(nameof(ColumnText));
            }
        }
    }

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

    public string AutomationName
    {
        get
        {
            var queued = _queuePosition is { } place ? $", queued number {place}" : "";
            return _detail.Length > 0
                ? $"{_scan.Name}, {SizeText} gigabytes{queued}. {_detail.Replace("\n", " ")}"
                : $"{_scan.Name}, {SizeText} gigabytes{queued}";
        }
    }

    /// <summary>The show's place in the queue, counting from 1, or null when it is not queued.</summary>
    public int? QueuePosition
    {
        get => _queuePosition;
        set
        {
            if (Set(ref _queuePosition, value))
            {
                Raise(nameof(Mark), nameof(QueueText), nameof(AutomationName));
            }
        }
    }

    /// <summary>What the row's menu offers.</summary>
    public string QueueText => _queuePosition is null ? "Add to queue" : "Remove from queue";

    /// <summary>Puts the show in the queue or takes it out. The deck sets it.</summary>
    public ICommand? QueueCommand { get; init; }

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

    /// <summary>A star marks the show in the deck, and a number a show's place in the queue.</summary>
    public string Mark => (_isLoaded ? "*" : "") + (_queuePosition?.ToString(CultureInfo.InvariantCulture) ?? "");

    public void Update(ShowScan scan, ShowSummary summary)
    {
        _scan = scan;
        _summary = summary;
        Raise(nameof(Scan), nameof(Summary), nameof(Name), nameof(SizeText), nameof(ColumnText), nameof(AutomationName));
    }
}
