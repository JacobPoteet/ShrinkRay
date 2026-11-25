using ShrinkRay.Core.Setup;

namespace ShrinkRay.Core.Tests.Setup;

public class ScratchCheckTests
{
    private const long Gb = 1_000_000_000;

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(100, false)]
    public void Space_is_low_under_twice_the_largest_episode(long freeGigabytes, bool low)
    {
        var check = ScratchCheck.For(@"C:\Scratch", @"X:\TV", 2 * Gb, freeGigabytes * Gb, onNetwork: false);

        Assert.Equal(low, check.IsLowOnSpace);
        Assert.True(check.IsUsable);
    }

    [Fact]
    public void An_unknown_free_space_is_not_called_low() =>
        Assert.False(ScratchCheck.For(@"C:\Scratch", "", 2 * Gb, null, onNetwork: false).IsLowOnSpace);

    [Theory]
    [InlineData(@"\\nas\media\scratch", true)]
    [InlineData(@"\\?\UNC\nas\media", true)]
    [InlineData(@"\\?\C:\Scratch", false)]
    [InlineData(@"C:\Scratch", false)]
    public void A_server_path_is_a_share(string folder, bool share)
    {
        Assert.Equal(share, ScratchCheck.IsNetworkShare(folder));
        Assert.Equal(!share, ScratchCheck.For(folder, "", Gb, null, onNetwork: false).IsUsable);
    }

    [Fact]
    public void A_mapped_network_drive_is_refused() =>
        Assert.False(ScratchCheck.For(@"Z:\Scratch", "", Gb, null, onNetwork: true).IsUsable);

    [Theory]
    [InlineData(@"X:\TV", @"X:\TV", true)]
    [InlineData(@"X:\TV\Scratch", @"X:\TV\", true)]
    [InlineData(@"x:/tv/scratch", @"X:\TV", true)]
    [InlineData(@"X:\TVScratch", @"X:\TV", false)]
    [InlineData(@"X:\Scratch", @"X:\TV", false)]
    [InlineData(@"X:\Scratch", "", false)]
    public void A_folder_inside_the_library_is_refused(string folder, string library, bool inside)
    {
        var check = ScratchCheck.For(folder, library, Gb, null, onNetwork: false);

        Assert.Equal(inside, check.IsInLibrary);
        Assert.Equal(!inside, check.IsUsable);
    }
}
