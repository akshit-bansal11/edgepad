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
    public void TheWindowWithTheTrackIsThePlayingOne() =>
        Assert.Equal(
            "Prime Video",
            BrowserTitle.Service(
                ["Stranger Things | Netflix - Google Chrome", "Prime Video: The Boys - Google Chrome"],
                "prime video: the boys"));

    [Fact]
    public void WithoutAMatchingTrackTheTopWindowIsThePlayingOne() =>
        Assert.Equal(
            "YouTube",
            BrowserTitle.Service(["Some Song - YouTube - Google Chrome", "Stranger Things | Netflix - Google Chrome"], "Other"));

    [Fact]
    public void AnEmptyTrackReadsTheTopWindow() =>
        Assert.Equal("Netflix", BrowserTitle.Service(["Stranger Things | Netflix - Google Chrome", "Prime Video - Chrome"], ""));

    [Fact]
    public void AnotherWindowNamingAServiceDoesNotNameThePlayingOne() =>
        Assert.Null(BrowserTitle.Service(["Inbox - Gmail - Google Chrome", "Stranger Things | Netflix - Google Chrome"], "Podcast"));

    [Fact]
    public void NoWindowsGivesNothing() => Assert.Null(BrowserTitle.Service([], "Track"));

    [Fact]
    public void TheSameAppLineAndWindowDoNotListTheWindowsAgain()
    {
        var listed = 0;
        var cache = new BrowserTitle.Cache(
            _ =>
            {
                listed++;
                return ["Stranger Things | Netflix - Google Chrome"];
            },
            () => "1|Stranger Things | Netflix - Google Chrome");

        Assert.Equal("Netflix", cache.Service("chrome", "Chrome", "Stranger Things", "Stranger Things"));
        Assert.Equal("Netflix", cache.Service("chrome", "Chrome", "Stranger Things", "Stranger Things"));
        Assert.Equal(1, listed);
    }

    [Fact]
    public void ANewLineOrAFrontWindowListsTheWindowsAgain()
    {
        var front = "1|Stranger Things | Netflix - Google Chrome";
        var titles = "Stranger Things | Netflix - Google Chrome";
        var listed = 0;
        var cache = new BrowserTitle.Cache(
            _ =>
            {
                listed++;
                return [titles];
            },
            () => front);
        _ = cache.Service("chrome", "Chrome", "Stranger Things", "Stranger Things");

        titles = "Some Song - YouTube - Google Chrome";
        Assert.Equal("YouTube", cache.Service("msedge", "MSEdge", "Stranger Things", "Stranger Things"));
        titles = "Prime Video: The Boys - Google Chrome";
        front = "1|Prime Video: The Boys - Google Chrome";
        Assert.Equal("Prime Video", cache.Service("msedge", "MSEdge", "Stranger Things", "Stranger Things"));
        Assert.Equal(3, listed);
    }
}
