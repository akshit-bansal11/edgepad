using Edgepad.Foreground;
using Xunit;

namespace Edgepad.Tests;

public sealed class AppKindsTests
{
    private static readonly string[] Kinds = ["media", "timeline", "browser"];
    private static readonly string[] Systems = ["windows", "linux", "macos"];

    [Fact]
    public void TheTableEqualsTheSharedFile()
    {
        var rows = Fixtures.Lines("app-kinds.txt").Where(line => line.StartsWith("APP_KIND ", StringComparison.Ordinal)).Select(line => line.Split(' ')).ToList();

        Assert.Equal(rows.Count, AppKinds.Count);
        foreach (var parts in rows)
        {
            Assert.Contains(parts[1], Kinds);
            Assert.Contains(parts[2], Systems);
            Assert.Equal(parts[1], AppKinds.Of(parts[2], parts[3]));
        }
    }

    [Fact]
    public void TheTokenIsMatchedIgnoringCaseAndPerOs()
    {
        Assert.Equal("browser", AppKinds.Of("windows", "CHROME.EXE"));
        Assert.Null(AppKinds.Of("linux", "chrome.exe"));
        Assert.Null(AppKinds.Of("windows", "notepad.exe"));
    }

    [Theory]
    [InlineData("timeline", true, "timeline")]
    [InlineData("media", false, "media")]
    [InlineData(null, true, "media")]
    [InlineData("browser", true, "media")]
    [InlineData("browser", false, "browser")]
    [InlineData(null, false, "")]
    public void ResolveFollowsTheOrderTableThenPlayingThenBrowser(string? tableKind, bool playing, string expected) =>
        Assert.Equal(expected, AppKinds.Resolve(tableKind, playing));

    [Fact]
    public void AKindIsSentOnlyWhenItChangesAndResetForgets()
    {
        List<string> sent = [];
        var kind = new ForegroundKind("windows", (token, source) => token == source, sent.Add);
        kind.SetForeground("chrome.exe");
        Assert.Empty(sent);

        kind.Start();
        kind.SetForeground("chrome.exe");
        kind.SetForeground("chrome.exe");
        kind.SetPlaying("chrome.exe");
        kind.SetPlaying(null);
        Assert.Equal(["browser", "media", "browser"], sent);

        kind.Reset();
        kind.SetForeground("chrome.exe");
        Assert.Equal(["browser", "media", "browser", "browser"], sent);
    }
}
