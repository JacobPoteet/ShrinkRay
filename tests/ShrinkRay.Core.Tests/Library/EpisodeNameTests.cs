using ShrinkRay.Core.Library;

namespace ShrinkRay.Core.Tests.Library;

public class EpisodeNameTests
{
    [Theory]
    [InlineData(@"X:\TV\Example Show\Season 01\Example Show - S01E03 - My Best Friend's Mistake.mkv", 1, 3, "My Best Friend's Mistake")]
    [InlineData(@"X:\TV\Example Show\Season 02\Example.Show.S02E10.The.Long.Night.mkv", 2, 10, "The Long Night")]
    [InlineData(@"X:\TV\Example Show\Season 01\Example Show s1e7.mp4", 1, 7, "")]
    [InlineData(@"X:\TV\Example Show\Season 01\Example Show - S01E01-E02 - Pilot.mkv", 1, 1, "Pilot")]
    [InlineData(@"X:\TV\Example Show\Season 03\Example Show 3x05 Title.mkv", 3, 5, "Title")]
    public void Reads_season_episode_and_title(string path, int season, int episode, string title)
    {
        var name = EpisodeName.Parse(path);

        Assert.Equal(season, name.Season);
        Assert.Equal(episode, name.Episode);
        Assert.Equal(title, name.Title);
    }

    [Fact]
    public void Code_pads_to_two_digits()
    {
        Assert.Equal("S01E03", EpisodeName.Parse(@"X:\TV\A\Season 01\A - S1E3.mkv").Code);
    }

    [Fact]
    public void Falls_back_to_the_season_folder_without_a_code()
    {
        var name = EpisodeName.Parse(@"X:\TV\Example Show\Season 04\Christmas Special.mkv");

        Assert.Equal(4, name.Season);
        Assert.Null(name.Episode);
        Assert.Equal("", name.Code);
        Assert.Equal("Christmas Special", name.Title);
    }

    [Theory]
    [InlineData("Season 01", 1)]
    [InlineData("season 12", 12)]
    [InlineData("Series 3", 3)]
    [InlineData("Specials", 0)]
    [InlineData("Extras", null)]
    public void Reads_season_folders(string folder, int? season)
    {
        Assert.Equal(season, EpisodeName.SeasonFromFolder(folder));
    }

    [Fact]
    public void Does_not_read_a_code_inside_a_word()
    {
        var name = EpisodeName.Parse(@"X:\TV\Example Show\Season 01\Mess01e02 notes.mkv");

        Assert.Null(name.Episode);
    }
}
