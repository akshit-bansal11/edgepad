using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>Middle truncation of file names in notices (DES-D29).</summary>
public sealed class SayTests
{
    [Fact]
    public void AShortNameIsUntouched() =>
        Assert.Equal("report.pdf", Say.MiddleTruncate("report.pdf"));

    [Fact]
    public void ALongNameKeepsItsExtension()
    {
        var shown = Say.MiddleTruncate("quarterly-report-final.pdf", 16);
        Assert.Equal(16, shown.Length);
        Assert.Equal("quarterly…al.pdf", shown);
    }

    [Fact]
    public void ANameWithoutAnExtensionIsCutAtTheEnd() =>
        Assert.Equal("abcdefghi…", Say.MiddleTruncate("abcdefghijklmnopqrstuvwxyz", 10));
}
