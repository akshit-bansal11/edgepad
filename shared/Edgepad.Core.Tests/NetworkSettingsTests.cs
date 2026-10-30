using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

public sealed class NetworkSettingsTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(dir, "network.txt");

    private NetworkSettings NewSettings() => new(FilePath);

    private void Write(string text)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, text);
    }

    [Fact]
    public void NoFileMeansOnWithNoChoiceMade()
    {
        Assert.Equal(new NetworkConfig(true, NetworkSettings.DefaultPort), NewSettings().Load());
        Assert.False(NewSettings().HasChoice);
    }

    [Fact]
    public void TurningItOffIsAChoiceThatStays()
    {
        Assert.True(NewSettings().SetEnabled(false));

        Assert.True(NewSettings().HasChoice);
        Assert.Equal(new NetworkConfig(false, NetworkSettings.DefaultPort), NewSettings().Load());
    }

    [Fact]
    public void TurningItOnIsKeptAcrossRestartsWithTheDefaultPort()
    {
        Assert.True(NewSettings().SetEnabled(true));

        Assert.Equal(new NetworkConfig(true, 47321), NewSettings().Load());

        Assert.True(NewSettings().SetEnabled(false));
        Assert.Equal(new NetworkConfig(false, 47321), NewSettings().Load());
    }

    [Fact]
    public void ACustomPortSurvivesTurningTheLinkOnAndOff()
    {
        Write("enabled=0\nport=5000\n");

        Assert.True(NewSettings().SetEnabled(true));

        Assert.Equal(new NetworkConfig(true, 5000), NewSettings().Load());
    }

    [Theory]
    [InlineData("enabled=2")]
    [InlineData("enabled=yes")]
    [InlineData("enabled=1\nport=0")]
    [InlineData("enabled=1\nport=65536")]
    [InlineData("enabled=1\nport=-1")]
    [InlineData("enabled=1\nport=")]
    [InlineData("enabled=1\nbogus=3")]
    [InlineData("enabled=1\njunk")]
    public void AFileEdgepadWouldNotHaveWrittenLeavesTheLinkOffAndIsNotRewritten(string content)
    {
        Write(content);

        Assert.Equal(new NetworkConfig(false, NetworkSettings.DefaultPort), NewSettings().Load());
        Assert.True(NewSettings().HasChoice);
        Assert.Equal(content, File.ReadAllText(FilePath));
    }

    [Fact]
    public void AFileThatCannotBeReadLeavesTheLinkOff()
    {
        // A folder where the file should be: reading it fails with an access error rather than "not found".
        Directory.CreateDirectory(FilePath);

        Assert.False(NewSettings().Load().Enabled);
        Assert.True(NewSettings().HasChoice);
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
