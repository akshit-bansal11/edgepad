using Edgepad.Transfer;
using Xunit;

namespace Edgepad.Tests;

/// <summary>Runs protocol/filenames.txt, which the Android and Mac suites run too, and what the file cannot hold.</summary>
public sealed class TransferNameTests
{
    public static TheoryData<string> Lines() => [.. Fixtures.Lines("filenames.txt")];

    [Theory]
    [MemberData(nameof(Lines))]
    public void ANameBecomesWhatTheFileSays(string line)
    {
        var split = line.LastIndexOf(" | ", StringComparison.Ordinal);
        var expected = line[(split + 3)..];
        Assert.Equal(expected == "(rejected)" ? null : expected, SafeFileName.Clean(line[..split]));
    }

    [Fact]
    public void ControlCharactersAreDropped()
    {
        Assert.Equal("ab.txt", SafeFileName.Clean("a\u0007b\r\n.txt"));
        Assert.Null(SafeFileName.Clean("\u0001\u0002"));
        Assert.Null(SafeFileName.Clean(null));
        Assert.Null(SafeFileName.Clean(""));
    }

    [Fact]
    public void ACutNeverSplitsACodePointAndKeepsTheExtension()
    {
        var name = SafeFileName.Clean(new string('é', 100) + ".pdf");
        Assert.NotNull(name);
        Assert.EndsWith(".pdf", name, StringComparison.Ordinal);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(name) <= TransferLimits.MaxNameBytes);
        Assert.DoesNotContain('�', name);
    }

    [Fact]
    public void AClashBecomesNumberedAndNeverTheSameName()
    {
        HashSet<string> taken = ["a.txt", "a (1).txt", "noext"];
        Assert.Equal("b.txt", SafeFileName.Unique("b.txt", taken.Contains));
        Assert.Equal("a (2).txt", SafeFileName.Unique("a.txt", taken.Contains));
        Assert.Equal("noext (1)", SafeFileName.Unique("noext", taken.Contains));
    }
}
