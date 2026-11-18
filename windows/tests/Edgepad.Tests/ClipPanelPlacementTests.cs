using System.Drawing;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>Where the clipboard panel opens. Its painting and keys need a desktop and are not tested here.</summary>
public sealed class ClipPanelPlacementTests
{
    private static readonly Rectangle Work = new(0, 0, 1920, 1080);
    private static readonly Size Panel = new(360, 440);

    [Fact]
    public void OpensBelowAndRightOfTheCursor() =>
        Assert.Equal(new Point(108, 108), ClipPanelPlacement.Place(new Point(100, 100), Panel, Work, 8));

    [Fact]
    public void FlipsLeftNearTheRightEdge() =>
        Assert.Equal(new Point(1432, 108), ClipPanelPlacement.Place(new Point(1800, 100), Panel, Work, 8));

    [Fact]
    public void FlipsUpNearTheBottom() =>
        Assert.Equal(new Point(108, 452), ClipPanelPlacement.Place(new Point(100, 900), Panel, Work, 8));

    [Fact]
    public void ATooBigPanelSitsAtTheOriginPlusMargin() =>
        Assert.Equal(new Point(8, 8), ClipPanelPlacement.Place(new Point(500, 500), new Size(3000, 2000), Work, 8));

    [Fact]
    public void WorksOnASecondMonitorLeftOfThePrimary() =>
        Assert.Equal(
            new Point(-468, 108),
            ClipPanelPlacement.Place(new Point(-100, 100), Panel, new Rectangle(-1920, 0, 1920, 1080), 8));

    [Theory]
    [InlineData(8)]
    [InlineData(20)]
    public void KeepsTheMarginFromTheEdges(int margin) =>
        Assert.Equal(new Point(margin, margin), ClipPanelPlacement.Place(new Point(-50, -50), Panel, Work, margin));
}
