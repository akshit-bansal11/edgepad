using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class LinuxOpenAppsTests
{
    private static readonly IReadOnlyList<string>[] Windows = [["inst", "Firefox"], ["inst", "Code"], ["inst", "My App"]];
    private static readonly string[] Macros = ["code", "Spotify"];
    private static readonly string[] Spotify = ["Spotify"];

    [Fact]
    public void WindowsAreListedNewestFirstByTheirLowerCasedClassThenMacroProgramsNotAlreadyThere() =>
        Assert.Equal("code\tCode\nfirefox\tFirefox\nspotify\tSpotify", LinuxOpenApps.Body(Windows, Macros));

    [Fact]
    public void WithoutAnyWindowsOnlyTheMacroProgramsAreSent() =>
        Assert.Equal("spotify\tSpotify", LinuxOpenApps.Body([], Spotify));
}
