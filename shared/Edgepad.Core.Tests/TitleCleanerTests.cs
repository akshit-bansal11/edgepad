using Edgepad.Media;
using Xunit;

namespace Edgepad.Tests;

public sealed class TitleCleanerTests
{
    public static TheoryData<string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var line in Fixtures.Lines("titles.txt"))
        {
            var parts = line.Split('|');
            Assert.Equal(3, parts.Length);
            data.Add(parts[0].Trim(), parts[1].Trim(), parts[2].Trim());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryFixtureLineIsCleanedAsWritten(string title, string artist, string expected) =>
        Assert.Equal(expected, TitleCleaner.Clean(title, artist));

    [Fact]
    public void AnEmptyTitleStaysEmpty() =>
        Assert.Equal("", TitleCleaner.Clean("", "Artist"));

    [Fact]
    public void NowPlayingJoinsTheCleanedTitleAndTheArtist() =>
        Assert.Equal("Song - Artist", TitleCleaner.NowPlaying("Song (Live)", "Artist"));

    [Fact]
    public void NowPlayingWithoutAnArtistIsTheCleanedTitle() =>
        Assert.Equal("Song", TitleCleaner.NowPlaying("Song (Live)", " "));
}
