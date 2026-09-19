using Edgepad.Controls;
using Xunit;

namespace Edgepad.Tests;

public sealed class BrowserTitleTests
{
    [Theory]
    [InlineData("chrome", true)]
    [InlineData("MSEdge", true)]
    [InlineData("Spotify", false)]
    public void OnlyBrowsersAreLookedUpByWindowTitle(string executable, bool browser) =>
        Assert.Equal(browser, BrowserTitle.IsBrowser(executable));

    [Fact]
    public void TheServiceIsReadFromTheTitle() =>
        Assert.Equal("Netflix", BrowserTitle.Service(["Stranger Things | Netflix - Google Chrome"]));

    [Fact]
    public void TheLongerNameWinsWhenOneContainsAnother() =>
        Assert.Equal("YouTube Music", BrowserTitle.Service(["Liked songs - YouTube Music - Google Chrome"]));

    /// <summary>
    /// The first title that names any service wins, and every visible browser window is searched — so a
    /// name matched inside an ordinary word outranks the tab that is actually playing. "Twitches" is on
    /// nobody's watchlist by accident; "Time complexity" would have named Plex the same way.
    /// </summary>
    [Fact]
    public void AWordThatMerelyContainsAServiceNameIsNotTheService() =>
        Assert.Equal(
            "YouTube",
            BrowserTitle.Service(
            [
                "Twitches (2005) - Letterboxd - Google Chrome",
                "Some Song - YouTube - Google Chrome",
            ]));

    /// <summary>Guards the trailing lookahead: a second \b would match neither "Paramount+" nor "Disney+".</summary>
    [Fact]
    public void AServiceEndingInANonWordCharacterIsStillFound() =>
        Assert.Equal("Paramount+", BrowserTitle.Service(["Yellowjackets | Paramount+ - Google Chrome"]));

    [Fact]
    public void PlexIsReadFromTheTitle() =>
        Assert.Equal("Plex", BrowserTitle.Service(["The Expanse | Plex - Google Chrome"]));

    [Fact]
    public void AnUnknownTitleGivesNothing() =>
        Assert.Null(BrowserTitle.Service(["New Tab - Google Chrome", "Inbox - Gmail"]));
}
