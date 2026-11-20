using Edgepad.Foreground;
using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class LinuxForegroundTests
{
    [Fact]
    public void TheClassIsTheSecondStringAndTheTokenIsLowerCased()
    {
        var app = LinuxForeground.ParseWmClass("inst\0Firefox\0");

        Assert.Equal("firefox", app.Token);
        Assert.Equal("Firefox", app.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0")]
    [InlineData("onlyone\0")]
    public void EmptyAndMalformedGiveNone(string text) => Assert.Equal(ForegroundApp.None, LinuxForeground.ParseWmClass(text));

    [Fact]
    public void ATokenWithIllegalCharactersGivesNone() =>
        Assert.Equal(ForegroundApp.None, LinuxForeground.ParseWmClass("inst\0My App\0"));
}
