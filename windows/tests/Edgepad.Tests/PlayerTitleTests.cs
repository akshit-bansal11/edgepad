using Edgepad.Controls;
using Xunit;

namespace Edgepad.Tests;

public sealed class PlayerTitleTests
{
    [Theory]
    [InlineData("chrome", true)]
    [InlineData("MSEdge", true)]
    [InlineData("Spotify", false)]
    public void OnlyBrowsersAreLookedUpByWindowTitle(string executable, bool browser) =>
        Assert.Equal(browser, PlayerTitle.IsBrowser(executable));

    [Fact]
    public void TheServiceIsReadFromTheTitle() =>
        Assert.Equal("Netflix", PlayerTitle.Service(["Stranger Things | Netflix - Google Chrome"]));

    [Fact]
    public void TheLongerNameWinsWhenOneContainsAnother() =>
        Assert.Equal("YouTube Music", PlayerTitle.Service(["Liked songs - YouTube Music - Google Chrome"]));

    [Fact]
    public void AnUnknownTitleGivesNothing() =>
        Assert.Null(PlayerTitle.Service(["New Tab - Google Chrome", "Inbox - Gmail"]));

    [Fact]
    public void TheMediaNameIsWhatIsLeftWhenThePlayerIsStrippedOff()
    {
        var playing = PlayerTitle.Playing(["Big Buck Bunny.mkv - VLC media player"]);
        Assert.Equal("VLC", playing?.App);
        Assert.Equal("Big Buck Bunny.mkv", playing?.Media);
    }

    [Fact]
    public void AnIdleVlcNamesNoMedia() => Assert.Null(PlayerTitle.Playing(["VLC media player"]));

    [Fact]
    public void AVlcWindowThatIsNotThePlayerNamesNoMedia() =>
        Assert.Null(PlayerTitle.Playing(["Simple Preferences", "Playlist"]));

    [Fact]
    public void ATitleThatIsNothingButThePlayerNamesNoMedia() =>
        Assert.Null(PlayerTitle.Playing([" - VLC media player"]));

    [Fact]
    public void TheFirstTitleThatNamesMediaWins()
    {
        var playing = PlayerTitle.Playing(
            ["VLC media player", "Second.mp4 - VLC media player", "Third.mp4 - VLC media player"]);
        Assert.Equal("Second.mp4", playing?.Media);
    }

    [Fact]
    public void ABrowserTitleIsNotMistakenForATitleOnlyPlayer() =>
        Assert.Null(PlayerTitle.Playing(["Stranger Things | Netflix - Google Chrome"]));
}
