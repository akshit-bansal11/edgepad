using Edgepad.Net;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The parts of the 2.0 UI that decide something rather than only paint: what the tray header says, whether the
/// icons come out where Lucide draws them, and whether Lato is really inside the exe.
/// </summary>
public sealed class UiTests
{
    [Fact]
    public void AConnectedPhoneReadsAsConnectedWithItsWay()
    {
        var status = TrayStatus.Describe(LaptopStatus.Connected("Pixel", PhoneWay.Bluetooth));

        Assert.Equal("Phone connected", status.Headline);
        Assert.Equal("Connected", status.Badge);
        Assert.Equal("Pixel · Bluetooth", status.Line);
        Assert.False(status.Waiting);
        Assert.True(status.Connected);
    }

    [Fact]
    public void AWiFiOrUsbPhoneKeepsTheWayItCame()
    {
        var status = TrayStatus.Describe(LaptopStatus.Connected("Pixel", PhoneWay.WifiOrUsb));

        Assert.Equal("Connected", status.Badge);
        Assert.Equal("Pixel · Wi-Fi or USB", status.Line);
    }

    [Fact]
    public void AnythingElseIsWaitingAndShowsTheStatusAsItIs()
    {
        LaptopStatus[] statuses = [LaptopStatus.Waiting, LaptopStatus.Starting, LaptopStatus.Asking("Pixel")];

        foreach (var line in statuses)
        {
            var status = TrayStatus.Describe(line);

            Assert.Equal("No phone connected", status.Headline);
            Assert.Equal("Waiting", status.Badge);
            Assert.Equal(line.Text, status.Line);
            Assert.True(status.Waiting);
            Assert.False(status.Problem);
        }
    }

    [Fact]
    public void ARefusalOrABrokenRadioIsAProblemNotWaiting()
    {
        LaptopStatus[] statuses = [LaptopStatus.RefusedUntrusted, LaptopStatus.BluetoothRetrying, LaptopStatus.RefusedProtocol(4, 5).For("Pixel", PhoneWay.Bluetooth)];

        foreach (var line in statuses)
        {
            var status = TrayStatus.Describe(line);

            Assert.Equal("Problem", status.Badge);
            Assert.Equal(line.Text, status.Line);
            Assert.True(status.Problem);
            Assert.False(status.Waiting);
            Assert.False(status.Connected);
        }
    }

    [Fact]
    public void ALongTooltipEndsOnAWordWithAnEllipsis()
    {
        var tip = TrayStatus.Tooltip("Pixel could not connect: its Edgepad app is older. Update Edgepad on the phone.", 63);

        Assert.True(tip.Length <= 63);
        Assert.EndsWith("…", tip);
        Assert.DoesNotContain("Updat…", tip);
    }

    [Fact]
    public void AShortTooltipIsLeftAlone() => Assert.Equal("Edgepad: Waiting for your phone", TrayStatus.Tooltip("Waiting for your phone", 63));

    /// <summary>A parser that got a sign, a relative offset or an arc's centre wrong puts the drawing off the grid.</summary>
    [Fact]
    public void EveryIconStaysOnItsTwentyFourUnitGrid()
    {
        var icons = typeof(Icons).GetFields().Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!);
        foreach (var data in icons)
        {
            using var path = Icons.Parse(data);
            path.Flatten();
            var bounds = path.GetBounds();

            Assert.InRange(bounds.Left, 0.5f, 24f);
            Assert.InRange(bounds.Top, 0.5f, 24f);
            Assert.InRange(bounds.Right, 0f, 23.5f);
            Assert.InRange(bounds.Bottom, 0f, 23.5f);
        }
    }

    /// <summary>
    /// The power symbol's arc takes the long way round a circle of radius 9 whose centre sits just below the
    /// grid's, near (12, 13). With the flags misread it would take the short way over the top instead, round a
    /// centre near (12, 0), and never come near the bottom of the grid.
    /// </summary>
    [Fact]
    public void AnArcGoesTheWayItsFlagsSay()
    {
        using var path = Icons.Parse(Icons.Power);
        path.Flatten();
        var bounds = path.GetBounds();

        Assert.InRange(bounds.Bottom, 21.8f, 22.1f);
        Assert.InRange(bounds.Left, 2.9f, 3.2f);
    }

    [Fact]
    public void LatoLoadsFromTheEmbeddedFonts() => Assert.True(Theme.LatoLoaded);
}
