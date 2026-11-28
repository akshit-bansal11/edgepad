using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class LinuxOpenAppsTests
{
    [Fact]
    public void WindowsAreListedNewestFirstByTheirLowerCasedClassThenMacroProgramsNotAlreadyThere()
    {
        var body = LinuxOpenApps.Body(
            [["inst", "Firefox"], ["inst", "Code"], ["inst", "My App"]],
            ["code", "Spotify"]);

        Assert.Equal("code\tCode\nfirefox\tFirefox\nspotify\tSpotify", body);
    }

    [Fact]
    public void WithoutAnyWindowsOnlyTheMacroProgramsAreSent() =>
        Assert.Equal("spotify\tSpotify", LinuxOpenApps.Body([], ["Spotify"]));
}
