using Edgepad.Controls;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The overlay's arithmetic and wording. The window itself needs a desktop, so only the parts that decide
/// what it says and where it sits are covered here.
/// </summary>
public sealed class LevelOverlayTests
{
    [Fact]
    public void EachKindIsNamedInSentenceCase()
    {
        Assert.Equal("Volume", LevelOverlay.Metrics.Label(LevelKind.Volume));
        Assert.Equal("Microphone", LevelOverlay.Metrics.Label(LevelKind.Microphone));
        Assert.Equal("Brightness", LevelOverlay.Metrics.Label(LevelKind.Brightness));
    }

    [Fact]
    public void TheValueIsTheBareNumber() =>
        Assert.Equal("45", LevelOverlay.Metrics.Caption(45, muted: false));

    [Fact]
    public void AMutedEndpointSaysSoInsteadOfShowingANumber() =>
        Assert.Equal("Muted", LevelOverlay.Metrics.Caption(45, muted: true));

    [Theory]
    [InlineData(-5, "0")]
    [InlineData(140, "100")]
    public void AValueOutsideTheRangeIsClampedRatherThanPrinted(int percent, string shown) =>
        Assert.Equal(shown, LevelOverlay.Metrics.Caption(percent, muted: false));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 100)]
    [InlineData(100, 200)]
    [InlineData(-1, 0)]
    [InlineData(101, 200)]
    public void TheBarFillsInProportionToTheLevel(int percent, int filled) =>
        Assert.Equal(filled, LevelOverlay.Metrics.BarWidth(200, percent));

    [Theory]
    [InlineData(96, 248)]
    [InlineData(144, 372)]
    [InlineData(192, 496)]
    public void MeasurementsScaleWithTheMonitorsDpi(int dpi, int pixels) =>
        Assert.Equal(pixels, LevelOverlay.Metrics.Scale(248, dpi));

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void ThePaddingIsTheSameOnAllFourSides(int dpi)
    {
        var pad = LevelOverlay.Metrics.Scale(18, dpi);
        var cap = LevelOverlay.Metrics.Scale(20, dpi) * LevelOverlay.Metrics.CapShare;
        var content = cap + LevelOverlay.Metrics.Scale(12, dpi) + LevelOverlay.Metrics.Scale(6, dpi);
        var height = LevelOverlay.Metrics.PanelHeight(pad, cap, LevelOverlay.Metrics.Scale(12, dpi), LevelOverlay.Metrics.Scale(6, dpi));

        // What is left above and below the content, split evenly, is the side padding to within a pixel's rounding.
        Assert.InRange((height - content) / 2, pad - 0.5, pad + 0.5);
    }

    [Fact]
    public void ThePanelIsCentredAboveTheBottomOfTheWorkingArea()
    {
        var at = LevelOverlay.Metrics.Anchor(new Rectangle(0, 0, 1920, 1040), new Size(248, 76), margin: 88);

        Assert.Equal(836, at.X);
        Assert.Equal(876, at.Y);
    }

    /// <summary>A taskbar docked left or top moves the working area's origin off zero; the panel follows it.</summary>
    [Fact]
    public void AnOffsetWorkingAreaMovesThePanelWithIt()
    {
        var at = LevelOverlay.Metrics.Anchor(new Rectangle(80, 40, 1840, 1040), new Size(248, 76), margin: 88);

        Assert.Equal(876, at.X);
        Assert.Equal(916, at.Y);
    }
}
