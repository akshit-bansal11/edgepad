using Edgepad.Media;
using Xunit;

namespace Edgepad.Tests;

public sealed class MediaServicesTests
{
    [Fact]
    public void TheTableEqualsTheSharedFile()
    {
        var rows = Fixtures.Lines("media-services.txt")
            .Where(line => line.StartsWith("SERVICE ", StringComparison.Ordinal))
            .Select(line => (Text: line["SERVICE ".Length..], NoLogo: line.Contains("# no logo", StringComparison.Ordinal)))
            .Select(row => (Body: row.Text.Split(" #")[0], row.NoLogo))
            .ToList();

        Assert.Equal(rows.Count, MediaServices.Rows.Length);
        for (var i = 0; i < rows.Count; i++)
        {
            var bar = rows[i].Body.IndexOf('|', StringComparison.Ordinal);
            var matches = rows[i].Body[(bar + 1)..].Split(',');
            var row = MediaServices.Rows[i];
            Assert.Equal(rows[i].Body[..bar], row.Name);
            Assert.Equal(matches.Where(m => m.StartsWith("title:", StringComparison.Ordinal)).Select(m => m[6..]), row.Titles);
            Assert.Equal(matches.Where(m => m.StartsWith("host:", StringComparison.Ordinal)).Select(m => m[5..]), row.Hosts);
            Assert.Equal(matches.Where(m => m.StartsWith("app:", StringComparison.Ordinal)).Select(m => m[4..]), row.Apps);
            Assert.Equal(rows[i].NoLogo, row.NoLogo);
        }
    }

    [Theory]
    [InlineData("Stranger Things | Netflix - Google Chrome", "Netflix")]
    [InlineData("Prime Video: The Boys - Microsoft Edge", "Prime Video")]
    [InlineData("Instagram - Google Chrome", "Instagram")]
    [InlineData("Liked songs - YouTube Music - Google Chrome", "YouTube Music")]
    [InlineData("Yellowjackets | Paramount+ - Google Chrome", "Paramount+")]
    [InlineData("Severance | Apple TV+ - Firefox", "Apple TV+")]
    [InlineData("The Expanse | Plex - Google Chrome", "Plex")]
    [InlineData("Time complexity", null)]
    [InlineData("Twitches (2005) - Letterboxd - Google Chrome", null)]
    [InlineData("New Tab - Google Chrome", null)]
    public void ATitleNamesTheServiceAsAWholeWord(string title, string? expected) =>
        Assert.Equal(expected, MediaServices.FromTitle(title));

    [Theory]
    [InlineData("music.youtube.com", "YouTube Music")]
    [InlineData("https://www.youtube.com/watch?v=abc", "YouTube")]
    [InlineData("www.netflix.com", "Netflix")]
    [InlineData("https://www.primevideo.com/detail/x", "Prime Video")]
    [InlineData("https://x.com/home", "X")]
    [InlineData("notnetflix.com", null)]
    [InlineData("example.org/netflix.com", null)]
    [InlineData("", null)]
    public void AHostIsTheServiceOrASubDomainOfIt(string address, string? expected) =>
        Assert.Equal(expected, MediaServices.FromHost(address));

    [Theory]
    [InlineData("AmazonVideo.PrimeVideo_pwbj9vvecjh7j!App", "Prime Video")]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("chrome.exe", null)]
    [InlineData("", null)]
    public void AnAppIdNamesTheService(string appId, string? expected) =>
        Assert.Equal(expected, MediaServices.FromApp(appId));
}
