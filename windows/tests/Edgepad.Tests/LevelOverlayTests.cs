using Edgepad.Controls;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The overlay's arithmetic and wording. The window itself needs a desktop, so only the parts that decide
/// what it says and where it sits are covered here.
/// </summary>
public sealed class LevelOverlayTests
{
    [Theory]
    [InlineData("volume", "VOLUME")]
    [InlineData("  Brightness  ", "BRIGHTNESS")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void TheLabelIsUppercasedAndTrimmed(string given, string shown) =>
        Assert.Equal(shown, LevelOverlay.Layout.Label(given));

    [Fact]
    public void TheValueReadsAsAPercentage() =>
        Assert.Equal("45%", LevelOverlay.Layout.Caption(45, muted: false));

    [Fact]
    public void AMutedEndpointSaysSoInsteadOfShowingANumber() =>
        Assert.Equal("MUTED", LevelOverlay.Layout.Caption(45, muted: true));

    [Theory]
    [InlineData(-5, "0%")]
    [InlineData(140, "100%")]
    public void AValueOutsideTheRangeIsClampedRatherThanPrinted(int percent, string shown) =>
        Assert.Equal(shown, LevelOverlay.Layout.Caption(percent, muted: false));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 100)]
    [InlineData(100, 200)]
    [InlineData(-1, 0)]
    [InlineData(101, 200)]
    public void TheBarFillsInProportionToTheLevel(int percent, int filled) =>
        Assert.Equal(filled, LevelOverlay.Layout.BarWidth(200, percent));

    [Theory]
    [InlineData(96, 248)]
    [InlineData(144, 372)]
    [InlineData(192, 496)]
    public void MeasurementsScaleWithTheMonitorsDpi(int dpi, int pixels) =>
        Assert.Equal(pixels, LevelOverlay.Layout.Scale(248, dpi));

    [Fact]
    public void ThePanelIsCentredAboveTheBottomOfTheWorkingArea()
    {
        var at = LevelOverlay.Layout.Anchor(new Rectangle(0, 0, 1920, 1040), new Size(248, 76), margin: 88);

        Assert.Equal(836, at.X);
        Assert.Equal(876, at.Y);
    }

    /// <summary>A taskbar docked left or top moves the working area's origin off zero; the panel follows it.</summary>
    [Fact]
    public void AnOffsetWorkingAreaMovesThePanelWithIt()
    {
        var at = LevelOverlay.Layout.Anchor(new Rectangle(80, 40, 1840, 1040), new Size(248, 76), margin: 88);

        Assert.Equal(876, at.X);
        Assert.Equal(916, at.Y);
    }
}
