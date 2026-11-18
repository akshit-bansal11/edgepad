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
    public void SendOptionTakesEveryFollowingPath()
    {
        Assert.Equal(["C:\\a b.txt"], SingleInstance.Parse(["--send", "C:\\a b.txt"]).SendPaths);
        Assert.Equal(["C:\\a b.txt", "C:\\c.txt", "D:\\d.txt"], SingleInstance.Parse(["--send", "C:\\a b.txt", "C:\\c.txt", "D:\\d.txt"]).SendPaths);
        Assert.Equal(["C:\\a.txt"], SingleInstance.Parse(["--send", "C:\\a.txt", "--restart", "S-1-5-21-1"]).SendPaths);
        Assert.Empty(SingleInstance.Parse(["--send"]).SendPaths);
        Assert.Empty(SingleInstance.Parse([]).SendPaths);
    }

    [Fact]
    public void ValidateAllCountsSkippedDedupesAndCaps()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "x");
            var (valid, skipped) = SendPipe.ValidateAll([file, "", "C:\\definitely\\not\\here.txt", file, null, "   "]);
            Assert.Equal([file], valid);
            Assert.Equal(1, skipped);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ValidateAllStopsAtTheCap()
    {
        var files = Enumerable.Range(0, SendPipe.MaxFiles + 20).Select(_ => Path.GetTempFileName()).ToList();
        try
        {
            foreach (var f in files)
            {
                File.WriteAllText(f, "x");
            }

            var (valid, skipped) = SendPipe.ValidateAll(files);
            Assert.Equal(SendPipe.MaxFiles, valid.Count);
            Assert.Equal(20, skipped);
        }
        finally
        {
            files.ForEach(File.Delete);
        }
    }
}
