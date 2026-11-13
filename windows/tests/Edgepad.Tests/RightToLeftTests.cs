using System.Globalization;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class RightToLeftTests
{
    private static readonly Rectangle Page = new(0, 0, 100, 50);
    private static readonly Rectangle Box = new(10, 5, 20, 10);

    [Fact]
    public void ArabicMirrorsABoxAcrossItsContainer()
    {
        using var _ = Speaking("ar");

        Assert.True(Theme.IsRtl);
        Assert.Equal(new Rectangle(70, 5, 20, 10), Theme.Flip(Box, Page));
        Assert.Equal(new RectangleF(70, 5, 20, 10), Theme.Flip(new RectangleF(10, 5, 20, 10), Page));
    }

    [Fact]
    public void EnglishLeavesTheBoxWhereItWas()
    {
        using var _ = Speaking("en");

        Assert.False(Theme.IsRtl);
        Assert.Equal(Box, Theme.Flip(Box, Page));
    }

    /// <summary>Switches the UI language for one test and puts it back.</summary>
    private static Restore Speaking(string name)
    {
        var before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        return new Restore(before);
    }

    private readonly struct Restore(CultureInfo before) : IDisposable
    {
        public void Dispose() => CultureInfo.CurrentUICulture = before;
    }
}
