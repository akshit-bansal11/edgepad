using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class RadiusTests
{
    [Theory]
    [InlineData(24, 8, 16)]
    [InlineData(8, 1, 7)]
    [InlineData(6, 8, 0)]
    public void TheInnerRadiusIsTheOuterMinusTheGap(float outer, float gap, float expected) =>
        Assert.Equal(expected, Theme.Inner(outer, gap));
}
