using ShrinkRay.App.Services;

namespace ShrinkRay.App.Tests;

public sealed class SingleInstanceTests
{
    // No folder is touched: the claim only reads the path, so each test makes up its own.
    private static string NewDataFolder() => Path.Combine(Path.GetTempPath(), "shrinkray-claim-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_second_copy_on_the_same_data_folder_is_refused()
    {
        var folder = NewDataFolder();
        using var first = SingleInstance.TryClaim(folder);

        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryClaim(folder));
    }

    [Fact]
    public void The_same_folder_spelled_another_way_is_refused()
    {
        var folder = NewDataFolder();
        using var first = SingleInstance.TryClaim(folder);

        Assert.Null(SingleInstance.TryClaim(folder.ToUpperInvariant() + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Copies_on_separate_data_folders_both_run()
    {
        using var first = SingleInstance.TryClaim(NewDataFolder());
        using var second = SingleInstance.TryClaim(NewDataFolder());

        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public void The_folder_can_be_claimed_again_once_the_first_copy_closes()
    {
        var folder = NewDataFolder();
        SingleInstance.TryClaim(folder)!.Dispose();

        using var next = SingleInstance.TryClaim(folder);

        Assert.NotNull(next);
    }

    [Fact]
    public void A_refused_copy_asks_the_first_to_show_itself()
    {
        var folder = NewDataFolder();
        using var asked = new ManualResetEventSlim();
        using var first = SingleInstance.TryClaim(folder)!;
        first.OnShowRequested(asked.Set);

        Assert.Null(SingleInstance.TryClaim(folder));

        Assert.True(asked.Wait(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void A_running_copy_holds_the_name_the_installer_checks()
    {
        using var first = SingleInstance.TryClaim(NewDataFolder());

        Assert.True(Mutex.TryOpenExisting(SingleInstance.RunningMutexName, out var marker));
        marker!.Dispose();
    }
}
