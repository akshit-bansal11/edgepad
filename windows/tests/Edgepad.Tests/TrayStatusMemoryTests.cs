using Edgepad.Net;
using Edgepad.Trust;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class TrayStatusMemoryTests
{
    [Fact]
    public void TheQuietPeriodIsSaidInPlaceOfWaiting()
    {
        var memory = new TrayStatusMemory();
        var quiet = LaptopStatus.QuietUntil("12:41");
        memory.Quiet(quiet, 5_000);

        Assert.Equal(quiet, memory.Resolve(LaptopStatus.Waiting, 1_000));
        Assert.Equal(LaptopStatus.Waiting, memory.Resolve(LaptopStatus.Waiting, 6_000));
    }

    [Theory]
    [InlineData(true, true, null)]
    [InlineData(false, true, "Wi-Fi and USB: could not listen. See the log.")]
    [InlineData(true, false, "Wi-Fi and USB: phones may not find this laptop.")]
    public void TheNetworkRowOnlySpeaksWhenSomethingIsWrong(bool listening, bool announced, string? expected) =>
        Assert.Equal(expected, NetworkStatus.Problem(listening, announced));

    [Fact]
    public void ABluetoothPhoneIsNamedByWindowsAndAPairedOneByItsPairing()
    {
        IReadOnlyList<(string, string)> pairings = [("00112233445566778899aabbccddeeff", "Tablet")];

        var bluetooth = PhoneEntry.Describe("(C0:35:32:26:23:F4)", pairings, _ => "Pixel");
        var paired = PhoneEntry.Describe("link:00112233445566778899AABBCCDDEEFF", pairings, _ => "unused");

        Assert.Equal(("Pixel", "Bluetooth", null), (bluetooth.Name, bluetooth.Way, bluetooth.LinkId));
        Assert.Equal(("Tablet", "Wi-Fi or USB", "00112233445566778899AABBCCDDEEFF"), (paired.Name, paired.Way, paired.LinkId));
    }
}
