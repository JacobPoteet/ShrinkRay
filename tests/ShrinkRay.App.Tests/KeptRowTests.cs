using ShrinkRay.App.ViewModels;
using ShrinkRay.Core.Library;

namespace ShrinkRay.App.Tests;

/// <summary>What a kept episode's tooltip says.</summary>
public class KeptRowTests
{
    [Theory]
    [InlineData("Saves 9.0%, below the 15.0% minimum", "Saves 9.0%, below the 15.0% minimum")]
    [InlineData("Replacing originals is off.", "Saves 9%. Replacing originals is off.")]
    public void A_kept_row_says_what_it_would_save_once(string reason, string expected)
    {
        var record = new EpisodeRecord(@"X:\TV\a.mkv", 1, 1, "S01E01", "", 100, 10, 25, "h264", true, "");
        var track = TrackViewModel.From(record, EpisodeStatus.Queued, null, null);

        track.SetKept(0.09, reason);

        Assert.Equal(expected, track.Detail);
    }
}
