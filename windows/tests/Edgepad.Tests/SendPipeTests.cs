using Edgepad.Startup;
using Edgepad.Transfer;
using Xunit;

namespace Edgepad.Tests;

public sealed class SendPipeTests
{
    [Fact]
    public void AnExistingFileIsAccepted()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "x");
            Assert.Equal(file, SendPipe.Validate(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\definitely\\not\\here.txt")]
    [InlineData("bad\0name")]
    public void AMissingOrEmptyPathIsRefused(string? line) => Assert.Null(SendPipe.Validate(line));

    [Fact]
    public void AFolderIsRefused() => Assert.Null(SendPipe.Validate(Path.GetTempPath()));

    [Fact]
    public void AnEmptyFileIsRefused()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.Null(SendPipe.Validate(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AHugeLineIsRefused() => Assert.Null(SendPipe.Validate(new string('a', 40_000)));

    [Fact]
    public void SendOptionTakesTheNextArgument()
    {
        Assert.Equal("C:\\a b.txt", SingleInstance.Parse(["--send", "C:\\a b.txt"]).SendPath);
        Assert.Null(SingleInstance.Parse(["--send"]).SendPath);
    }
}
