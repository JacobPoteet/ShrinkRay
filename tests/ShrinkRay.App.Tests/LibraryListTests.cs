using ShrinkRay.App.Services;
using ShrinkRay.App.ViewModels;
using ShrinkRay.Core.Library;

namespace ShrinkRay.App.Tests;

/// <summary>Monitor A's search and order, and the estimates behind them.</summary>
public class LibraryListTests
{
    private static DeckRig Library()
    {
        var rig = new DeckRig();
        rig.Add("Alpha", 1, 1, size: DeckRig.Gigabyte);
        rig.Add("Bravo", 1, 1, size: 4 * DeckRig.Gigabyte);
        rig.Add("Charlie", 1, 1, size: 2 * DeckRig.Gigabyte);
        return rig;
    }

    private static IEnumerable<string> Listed(MainViewModel vm) => vm.VisibleShows.Select(r => r.FolderName);

    [Fact]
    public async Task The_list_is_by_name_until_the_sort_key_puts_the_most_to_save_first()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Alpha");
        Assert.Equal(["Alpha", "Bravo", "Charlie"], Listed(vm));
        Assert.Equal("A to Z", vm.SortText);
        Assert.Equal("4.0", vm.VisibleShows[1].ColumnText);

        vm.ToggleSortCommand.Execute(null);

        Assert.Equal(["Bravo", "Charlie", "Alpha"], Listed(vm));
        Assert.Equal("Most to save", vm.SortText);
        Assert.Equal("2.6", vm.VisibleShows[0].ColumnText);
        Assert.True(rig.SavedPreferences!.SortBySavings);
        Assert.Equal("Alpha", vm.SelectedShow?.FolderName);

        var again = rig.Create(rig.SavedPreferences);
        await again.StartAsync();
        Assert.Equal(["Bravo", "Charlie", "Alpha"], Listed(again));
    }

    [Fact]
    public async Task A_shows_own_results_move_it_in_the_savings_order()
    {
        var rig = Library();

        // Bravo's episodes barely shrink, so its 4 GB are worth less than Charlie's 2 GB.
        rig.Ledger.Record(new LedgerEntry(DeckRig.Episode("Bravo", 1, 9), 1000, 900, 28, DateTimeOffset.UnixEpoch));
        var vm = rig.Create(new DeckPreferences { SortBySavings = true });
        await vm.StartAsync();

        Assert.Equal(["Charlie", "Alpha", "Bravo"], Listed(vm));
        Assert.Contains("going by 1 of its episodes", vm.VisibleShows[2].Detail);
    }

    [Fact]
    public async Task The_search_narrows_the_list_and_clearing_it_brings_the_cursor_back()
    {
        var rig = Library();
        var vm = await rig.StartedWith("Bravo");

        vm.Search = " CHAR ";

        Assert.Equal(["Charlie"], Listed(vm));
        Assert.Equal("", vm.MonitorMessage);
        Assert.Equal("Bravo", vm.LoadedName);
        Assert.Equal(3, vm.Shows.Count);

        vm.Search = "zulu";
        Assert.Empty(vm.VisibleShows);
        Assert.Equal("NO SHOW MATCHES", vm.MonitorMessage);

        // The list box drops its selection when the row leaves.
        vm.SelectedShow = null;
        vm.ClearSearchCommand.Execute(null);
        Assert.Equal(["Alpha", "Bravo", "Charlie"], Listed(vm));
        Assert.Equal("Bravo", vm.SelectedShow?.FolderName);
        Assert.False(vm.ClearSearchCommand.CanExecute(null));
    }
}
