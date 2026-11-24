using ShrinkRay.Core.Media;

namespace ShrinkRay.Core.Tests.Media;

public class ShowFolderTests
{
    [Theory]
    [InlineData(@"X:\TV\Example Show\Season 01\Example Show S01E01.mkv", @"X:\TV", "Example Show")]
    [InlineData(@"X:\TV\Example Show\Season 01\Example Show S01E01.mkv", @"X:\TV\", "Example Show")]
    [InlineData(@"X:\TV\Example Show\Extras\Disc 1\Clip.mkv", @"x:\tv", "Example Show")]
    [InlineData(@"X:\TV\Example Show\Season 01\Example Show S01E01.mkv", "", "Example Show")]
    [InlineData(@"X:\TV\Example Show\Specials\Example Show S00E01.mkv", "", "Example Show")]
    [InlineData(@"X:\TV\Example Show\S2\Example Show S02E01.mkv", "", "Example Show")]
    [InlineData(@"X:\TV\Example Show\Example Show S01E01.mkv", "", "Example Show")]
    [InlineData(@"X:\Other\Example Show\Season 03\Ep.mkv", @"X:\TV", "Example Show")]
    [InlineData(@"X:\TV\Example Show\Season 1\Example Show S01E01.mkv", @"X:\TV\Example Show", "Example Show")]
    [InlineData(@"X:\TV\Example Show\Specials\Example Show S00E01.mkv", @"X:\TV\Example Show\", "Example Show")]
    [InlineData(@"X:\TV\Example Show\Series 2\Example Show S02E01.mkv", @"X:\TV\Example Show", "Example Show")]
    [InlineData(@"X:\TV\Example Show\Example Show S01E01.mkv", @"X:\TV\Example Show", "Example Show")]
    public void Finds_the_show_folder(string episode, string root, string expected)
    {
        Assert.Equal(expected, ShowFolder.NameFor(episode, root));
    }

    [Fact]
    public void Episode_straight_under_the_root_uses_its_folder()
    {
        Assert.Equal("TV", ShowFolder.NameFor(@"X:\TV\Loose Episode.mkv", @"X:\TV"));
    }

    [Fact]
    public void Bare_file_name_has_no_show()
    {
        Assert.Null(ShowFolder.NameFor("Episode.mkv", null));
    }
}
