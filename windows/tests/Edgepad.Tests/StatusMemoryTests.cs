using Edgepad.Trust;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class StatusMemoryTests
{
    private const string Mismatch = "Pixel could not connect: its Edgepad app is older. Update Edgepad on the phone.";

    [Fact]
    public void ARefusalOutlivesTheWaitingThatFollowsIt()
    {
        var memory = new StatusMemory();
        memory.Resolve(Mismatch, 0);

        Assert.Equal(Mismatch, memory.Resolve("Waiting for your phone", 1_000));
    }

    [Fact]
    public void ARefusalExpiresAndAConnectedPhoneClearsIt()
    {
        var memory = new StatusMemory();
        memory.Resolve(Mismatch, 0);
        Assert.Equal("Waiting for your phone", memory.Resolve("Waiting for your phone", (long)StatusMemory.Sticky.TotalMilliseconds + 1));

        memory.Resolve(Mismatch, 0);
        memory.Resolve("Connected to Pixel · Bluetooth", 10);
        Assert.Equal("Waiting for your phone", memory.Resolve("Waiting for your phone", 20));
    }

    [Fact]
    public void AMismatchIsAnnouncedOncePerCausePerHour()
    {
        var memory = new StatusMemory();

        Assert.True(memory.ShouldAnnounce(Mismatch, 0));
        Assert.False(memory.ShouldAnnounce(Mismatch, 1_000));
        Assert.True(memory.ShouldAnnounce(Mismatch.Replace("Pixel", "Tablet", StringComparison.Ordinal), 1_000));
        Assert.True(memory.ShouldAnnounce(Mismatch, (long)StatusMemory.AnnounceEvery.TotalMilliseconds + 1));
        Assert.False(memory.ShouldAnnounce("Refused a phone it does not trust", 0));
    }

    [Fact]
    public void TheQuietPeriodIsSaidInPlaceOfWaiting()
    {
        var memory = new StatusMemory();
        memory.Quiet("Phones can connect again at 12:41", 5_000);

        Assert.Equal("Phones can connect again at 12:41", memory.Resolve("Waiting for your phone", 1_000));
        Assert.Equal("Waiting for your phone", memory.Resolve("Waiting for your phone", 6_000));
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
