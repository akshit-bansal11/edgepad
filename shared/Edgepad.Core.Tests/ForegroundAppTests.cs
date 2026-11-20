using Edgepad.Foreground;
using Xunit;

namespace Edgepad.Tests;

public sealed class ForegroundAppTests
{
    [Fact]
    public void NoneEncodesAsEmptyText() => Assert.Equal("", ForegroundApp.Encode(ForegroundApp.None));

    [Fact]
    public void AppEncodesAsTokenSlashName() =>
        Assert.Equal("chrome.exe/Chrome", ForegroundApp.Encode(ForegroundApp.Clean("chrome.exe", "Chrome")));

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("a/b")]
    [InlineData("a|b")]
    [InlineData("café")]
    public void ATokenOutsideTheRuleBecomesNone(string token) =>
        Assert.Equal(ForegroundApp.None, ForegroundApp.Clean(token, "Name"));

    [Fact]
    public void ATokenOver64CharactersBecomesNone()
    {
        Assert.Equal(64, ForegroundApp.Clean(new string('a', 64), "x").Token.Length);
        Assert.Equal(ForegroundApp.None, ForegroundApp.Clean(new string('a', 65), "x"));
    }

    [Fact]
    public void ControlCharactersAndBarsBecomeSpacesAndTheNameIsTrimmed() =>
        Assert.Equal("a b c d", ForegroundApp.Clean("t", "  a|b\tc\nd \r").Name);

    [Fact]
    public void ANullNameIsEmpty() => Assert.Equal("t/", ForegroundApp.Encode(ForegroundApp.Clean("t", null)));

    [Fact]
    public void TheNameIsCutTo40CodePoints() =>
        Assert.Equal(40, ForegroundApp.Clean("t", new string('n', 100)).Name.Length);

    [Fact]
    public void TheCutNeverSplitsASurrogatePair()
    {
        var name = new string('n', 39) + "\U0001F600" + "tail";

        var cleaned = ForegroundApp.Clean("t", name).Name;

        Assert.Equal(new string('n', 39) + "\U0001F600", cleaned);
        Assert.Equal(41, cleaned.Length);
    }

    [Fact]
    public void ASlashInTheNameSurvives() =>
        Assert.Equal("t/Notes/Today", ForegroundApp.Encode(ForegroundApp.Clean("t", "Notes/Today")));
}
