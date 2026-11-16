using Edgepad.Controls;
using Xunit;

namespace Edgepad.Tests;

public sealed class MediaIconTests
{
    [Theory]
    [InlineData(true, true, true, MediaIcon.Pick.Thumbnail)]
    [InlineData(true, true, false, MediaIcon.Pick.Thumbnail)]
    [InlineData(true, false, true, MediaIcon.Pick.AppIcon)]
    [InlineData(false, true, true, MediaIcon.Pick.AppIcon)]
    [InlineData(false, true, false, MediaIcon.Pick.None)]
    [InlineData(false, false, false, MediaIcon.Pick.None)]
    public void ChoosePrefersBrowserArtworkThenTheExe(bool browser, bool thumbnail, bool exe, MediaIcon.Pick expected) =>
        Assert.Equal(expected, MediaIcon.Choose(browser, thumbnail, exe));

    [Fact]
    public void ArtworkIsLabelledArtworkAndAnAppByItsName()
    {
        Assert.Equal("artwork", MediaIcon.Label(MediaIcon.Pick.Thumbnail, "chrome.exe"));
        Assert.Equal("Spotify", MediaIcon.Label(MediaIcon.Pick.AppIcon, "Spotify.exe"));
    }

    [Fact]
    public void TheKeyFollowsTheTrackInABrowserAndTheAppElsewhere()
    {
        var browser = new MediaState("a", "Chrome", true, 0, 0, 0, "A - B", "chrome.exe");
        Assert.NotEqual(MediaIcon.Key(browser), MediaIcon.Key(browser with { NowPlayingFull = "C - D" }));
        var player = new MediaState("a", "Spotify", true, 0, 0, 0, "A - B", "Spotify.exe");
        Assert.Equal(MediaIcon.Key(player), MediaIcon.Key(player with { NowPlayingFull = "C - D" }));
        Assert.Equal("", MediaIcon.Key(MediaSessions.Nothing));
    }
}
