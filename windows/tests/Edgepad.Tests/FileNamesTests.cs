using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class FileNamesTests
{
    [Theory]
    [InlineData("report.pdf", 20, "report.pdf")]
    [InlineData("quarterly-report-final.pdf", 20, "quarterly-2026final.pdf")]
    [InlineData("a-very-long-name-without-an-extension", 12, "a-very-l2026ion")]
    [InlineData("abcdefghij.verylongextension", 10, "abcdef2026ion")]
    public void LongNamesAreCutInTheMiddleAndKeepTheirExtension(string name, int max, string expected)
    {
        var cut = FileNames.Middle(name, max);

        Assert.Equal(expected, cut);
        Assert.True(cut.Length <= max);
    }
}
