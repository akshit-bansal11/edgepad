using Edgepad.Controls;
using Xunit;

namespace Edgepad.Tests;

public sealed class VlcTitleTests
{
    [Theory]
    [InlineData("song.mp3 - VLC media player", "song.mp3")]
    [InlineData("Artist - Song.mp3 - VLC media player", "Artist - Song.mp3")]
    [InlineData("VLC media player", null)]
    [InlineData("", null)]
    [InlineData("Notepad", null)]
    public void ParseTakesTheMediaTitleOrNothing(string title, string? expected) =>
        Assert.Equal(expected, VlcTitle.Parse(title));
}
