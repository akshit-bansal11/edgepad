using Edgepad.Controls;
using Xunit;

namespace Edgepad.Tests;

public sealed class ForegroundNamingTests
{
    [Theory]
    [InlineData("C:\Program Files\Google\Chrome\Application\Chrome.EXE", "chrome.exe")]
    [InlineData("notepad.exe", "notepad.exe")]
    [InlineData("D:\apps\Code-Insiders_1.exe", "code-insiders_1.exe")]
    public void TheTokenIsTheLowerCasedFileNameWithoutItsDirectory(string path, string expected) =>
        Assert.Equal(expected, ForegroundNaming.Token(path));

    [Theory]
    [InlineData("C:\apps\my app.exe")]
    [InlineData("C:\apps\caf\u00e9.exe")]
    [InlineData("")]
    public void AFileNameThatIsNotALegalTokenGivesNone(string path) =>
        Assert.Equal("", ForegroundNaming.Token(path));

    [Fact]
    public void TheDescriptionBeatsTheFileName() =>
        Assert.Equal("Google Chrome", ForegroundNaming.Name("C:\x\chrome.exe", " Google Chrome "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyDescriptionFallsBackToTheFileNameWithoutItsExtension(string? description) =>
        Assert.Equal("chrome", ForegroundNaming.Name("C:\x\chrome.exe", description));
}
