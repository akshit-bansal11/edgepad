using Edgepad.Net;
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
    public void AForgetEndsTheHeldRefusal()
    {
        var memory = new StatusMemory();
        memory.Resolve(LaptopStatus.RefusedPin, 0);
        memory.Clear();

        Assert.Equal(LaptopStatus.Waiting, memory.Resolve(LaptopStatus.Waiting, 1_000));
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
}
