using Xunit;

namespace Edgepad.Tests;

public sealed class XdgTests
{
    [Fact]
    public void AnAbsoluteVariableWins() =>
        Assert.Equal("/srv/conf", Xdg.Resolve("/srv/conf", "/home/me", ".config"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    // The spec calls a relative path invalid, to be ignored.
    [InlineData("conf")]
    public void OtherwiseTheFolderUnderHome(string? value) =>
        Assert.Equal(Path.Combine("/home/me", ".config"), Xdg.Resolve(value, "/home/me", ".config"));
}
