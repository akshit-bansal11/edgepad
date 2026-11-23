using Edgepad.Controls;
using Xunit;

namespace Edgepad.Tests;

public sealed class MediaTitleTests
{
    [Theory]
    [InlineData("Song (Live)", "Artist", "Song - Artist", "Song (Live) - Artist")]
    [InlineData("Song", "Artist", "Song - Artist", "Song - Artist")]
    [InlineData("Song (Live)", "", "Song", "Song (Live)")]
    [InlineData("Song", " ", "Song", "Song")]
    [InlineData("", "", "", "")]
    public void ComposeGivesTheCleanedAndTheFullLine(string title, string artist, string cleaned, string full) =>
        Assert.Equal((cleaned, full), MediaSessions.Compose(title, artist));
}
