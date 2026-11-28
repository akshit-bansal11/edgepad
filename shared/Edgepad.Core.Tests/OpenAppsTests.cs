using Edgepad.Foreground;
using Xunit;

namespace Edgepad.Tests;

public sealed class OpenAppsTests
{
    [Fact]
    public void OpenAppsComeFirstThenMacroAppsNotAlreadyListed()
    {
        var body = OpenApps.Build(
            [ForegroundApp.Clean("code.exe", "Code"), ForegroundApp.Clean("spotify.exe", "Spotify")],
            [ForegroundApp.Clean("spotify.exe", "Spotify again"), ForegroundApp.Clean("notepad.exe", "Notepad")]);

        Assert.Equal("code.exe\tCode\nspotify.exe\tSpotify\nnotepad.exe\tNotepad", body);
    }

    [Fact]
    public void ABadTokenIsSkippedAndANameNeverCarriesATabOrLineBreak()
    {
        var body = OpenApps.Build([ForegroundApp.Clean("bad token", "x"), ForegroundApp.Clean("a.exe", "A\tB\nC")], []);

        Assert.Equal("a.exe\tA B C", body);
    }

    [Fact]
    public void TheListStopsAtFortyLinesAndNothingIsEmptyText()
    {
        var many = Enumerable.Range(0, 60).Select(i => ForegroundApp.Clean($"app{i}.exe", $"App {i}")).ToList();

        Assert.Equal(OpenApps.MaxLines, OpenApps.Build(many, []).Split('\n').Length);
        Assert.Equal("", OpenApps.Build([], []));
    }
}
