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

    [Fact]
    public void AnUnknownTitleGivesNothing() =>
        Assert.Null(BrowserTitle.Service(["New Tab - Google Chrome", "Inbox - Gmail"]));
}
