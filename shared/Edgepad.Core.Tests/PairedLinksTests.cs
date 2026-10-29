using Edgepad.Trust;
using Xunit;

namespace Edgepad.Tests;

public sealed class PairedLinksTests : IDisposable
{
    private const string Phone = "00112233445566778899aabbccddeeff";
    private const string Other = "ffeeddccbbaa99887766554433221100";

    private static readonly byte[] Key = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private static readonly byte[] NewerKey = [.. Enumerable.Range(100, 32).Select(i => (byte)i)];

    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(dir, "paired-links.txt");

    private PairedLinks NewLinks() => new(FilePath);

    [Fact]
    public void APairingIsKeptAcrossRestarts()
    {
        Assert.True(NewLinks().Add(Phone, Key, "Pixel"));

        Assert.True(NewLinks().TryGet(Phone.ToUpperInvariant(), out var key, out var name));
        Assert.Equal(Key, key);
        Assert.Equal("Pixel", name);
        Assert.False(NewLinks().TryGet(Other, out _, out _));
    }

    [Fact]
    public void PairingAgainReplacesTheOldKey()
    {
        var links = NewLinks();
        links.Add(Phone, Key, "Pixel");
        links.Add(Other, Key, "Tablet");
        links.Add(Phone, NewerKey, "Pixel 2");

        Assert.True(links.TryGet(Phone, out var key, out var name));
        Assert.Equal(NewerKey, key);
        Assert.Equal("Pixel 2", name);
        Assert.Equal(2, File.ReadAllLines(FilePath).Length);
    }

    [Fact]
    public void ANameCannotBreakItsLine()
    {
        Assert.True(NewLinks().Add(Phone, Key, "evil\nffeeddccbbaa99887766554433221100\tAAAA\tx"));

        Assert.Single(File.ReadAllLines(FilePath));
        Assert.False(NewLinks().TryGet(Other, out _, out _));
    }

    [Fact]
    public void AnUnreadableFileRefusesEveryPhoneAndIsNotWrittenOver()
    {
        var links = NewLinks();
        links.Add(Phone, Key, "Pixel");

        using (new Blocked(FilePath))
        {
            Assert.False(links.TryGet(Phone, out _, out _));
            Assert.False(links.Add(Other, Key, "Tablet"));
            Assert.True(links.CanForget);
        }

        Assert.True(links.TryGet(Phone, out _, out _));
        Assert.False(links.TryGet(Other, out _, out _));
    }

    [Fact]
    public void ALineEdgepadWouldNotWriteRefusesEveryPhone()
    {
        NewLinks().Add(Phone, Key, "Pixel");
        File.AppendAllText(FilePath, "not a pairing\n");

        Assert.False(NewLinks().TryGet(Phone, out _, out _));
    }

    [Fact]
    public void ForgetClearsEveryPairing()
    {
        var links = NewLinks();
        Assert.False(links.CanForget);
        links.Add(Phone, Key, "Pixel");
        Assert.True(links.CanForget);

        Assert.True(links.Forget());
        Assert.False(links.TryGet(Phone, out _, out _));
        Assert.False(links.CanForget);
    }

    [Fact]
    public void OnUnixTheFileIsPrivateToItsOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        NewLinks().Add(Phone, Key, "Pixel");

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FilePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
