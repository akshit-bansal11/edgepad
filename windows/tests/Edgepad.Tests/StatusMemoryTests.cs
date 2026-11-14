using Edgepad.Net;
using Edgepad.Trust;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class StatusMemoryTests
{
    private static readonly LaptopStatus Mismatch = LaptopStatus.RefusedProtocol(4, 5).For("Pixel", PhoneWay.Bluetooth);

    [Fact]
    public void ARefusalOutlivesTheWaitingThatFollowsIt()
    {
        var memory = new StatusMemory();
        memory.Resolve(Mismatch, 0);

        Assert.Equal(Mismatch, memory.Resolve(LaptopStatus.Waiting, 1_000));
    }

    [Fact]
    public void ARefusalExpiresAndAConnectedPhoneClearsIt()
    {
        var memory = new StatusMemory();
        memory.Resolve(Mismatch, 0);
        Assert.Equal(LaptopStatus.Waiting, memory.Resolve(LaptopStatus.Waiting, (long)StatusMemory.Sticky.TotalMilliseconds + 1));

        memory.Resolve(Mismatch, 0);
        memory.Resolve(LaptopStatus.Connected("Pixel", PhoneWay.Bluetooth), 10);
        Assert.Equal(LaptopStatus.Waiting, memory.Resolve(LaptopStatus.Waiting, 20));
    }

    [Fact]
    public void ARadioConditionEndsAtOnceWhenTheRadioRecovers()
    {
        var memory = new StatusMemory();

        Assert.Equal(LaptopStatus.BluetoothOff, memory.Resolve(LaptopStatus.BluetoothOff, 0));
        Assert.Equal(LaptopStatus.Waiting, memory.Resolve(LaptopStatus.Waiting, 1_000));
    }

    [Fact]
    public void ARadioConditionDoesNotEraseARefusalStillHeld()
    {
        var memory = new StatusMemory();
        memory.Resolve(Mismatch, 0);
        memory.Resolve(LaptopStatus.BluetoothOff, 500);

        Assert.Equal(Mismatch, memory.Resolve(LaptopStatus.Waiting, 1_000));
    }

    [Fact]
    public void AMismatchIsAnnouncedOncePerCausePerHour()
    {
        var memory = new StatusMemory();

        Assert.True(memory.ShouldAnnounce(Mismatch, 0));
        Assert.False(memory.ShouldAnnounce(Mismatch, 1_000));
        Assert.True(memory.ShouldAnnounce(LaptopStatus.RefusedProtocol(4, 5).For("Tablet", PhoneWay.Bluetooth), 1_000));
        Assert.True(memory.ShouldAnnounce(Mismatch, (long)StatusMemory.AnnounceEvery.TotalMilliseconds + 1));
        Assert.False(memory.ShouldAnnounce(LaptopStatus.RefusedUntrusted, 0));
    }

    [Fact]
    public void TheQuietPeriodIsSaidInPlaceOfWaiting()
    {
        var memory = new StatusMemory();
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
