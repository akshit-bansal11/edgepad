using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Edgepad.Input;
using Xunit;

namespace Edgepad.Tests;

public sealed class UinputTests
{
    [Fact]
    public void AnInputEventIsTheKernelsTwentyFourBytes()
    {
        Assert.Equal(24, Unsafe.SizeOf<InputEvent>());

        // timeval (16 bytes, zero), then type, code and value, little-endian.
        InputEvent[] one = [new(0x0102, 0x0304, -2)];
        var bytes = MemoryMarshal.AsBytes(one.AsSpan()).ToArray();
        Assert.Equal(new byte[16], bytes[..16]);
        Assert.Equal(new byte[] { 0x02, 0x01, 0x04, 0x03, 0xFE, 0xFF, 0xFF, 0xFF }, bytes[16..]);
    }

    [Fact]
    public void UinputSetupIsTheKernelsNinetyTwoBytes()
    {
        Assert.Equal(92, Unsafe.SizeOf<UinputSetup>());

        var setup = UinputSetup.Create(new InputId(0x0006, 0x045E, 0x028E, 0x0110), "Edgepad");
        var bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<UinputSetup>(ref setup)).ToArray();
        Assert.Equal(new byte[] { 0x06, 0x00, 0x5E, 0x04, 0x8E, 0x02, 0x10, 0x01 }, bytes[..8]);
        Assert.Equal("Edgepad\0"u8.ToArray(), bytes[8..16]);
        // ff_effects_max, at 88, stays zero.
        Assert.Equal(new byte[4], bytes[88..]);
    }

    [Fact]
    public void ALongNameKeepsItsTerminator()
    {
        var setup = UinputSetup.Create(default, new string('x', 200));
        var bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<UinputSetup>(ref setup)).ToArray();
        Assert.Equal((byte)'x', bytes[8 + 78]);
        Assert.Equal(0, bytes[8 + 79]);
    }

    // Recomputed here from asm-generic/ioctl.h's _IOC(dir, type, nr, size), independently of the constants' comment.
    private static uint Ioc(uint dir, uint nr, uint size) => (dir << 30) | (size << 16) | ((uint)'U' << 8) | nr;

    [Fact]
    public void IoctlNumbersMatchTheHeaderMacros()
    {
        Assert.Equal(Ioc(0, 1, 0), Uinput.DevCreate);
        Assert.Equal(Ioc(0, 2, 0), Uinput.DevDestroy);
        Assert.Equal(Ioc(1, 3, 92), Uinput.DevSetup);
        Assert.Equal(Ioc(1, 100, 4), Uinput.SetEvBit);
        Assert.Equal(Ioc(1, 101, 4), Uinput.SetKeyBit);
        Assert.Equal(Ioc(1, 102, 4), Uinput.SetRelBit);
        Assert.Equal(0x405C5503u, Uinput.DevSetup);
        Assert.Equal(0x40045564u, Uinput.SetEvBit);
    }

    [Fact]
    public void WholeNotchesGoToTheLegacyWheel()
    {
        var wheel = default(Wheel);
        Assert.Equal(1, wheel.Add(120));
        Assert.Equal(-2, wheel.Add(-240));
    }

    [Fact]
    public void SmallAmountsAddUpToANotch()
    {
        var wheel = default(Wheel);
        Assert.Equal(0, wheel.Add(50));
        Assert.Equal(0, wheel.Add(50));
        Assert.Equal(1, wheel.Add(50));
        // 30 left over, so 90 more is the next notch.
        Assert.Equal(1, wheel.Add(90));
    }

    [Fact]
    public void TurningBackUsesUpTheRemainderFirst()
    {
        var wheel = default(Wheel);
        Assert.Equal(0, wheel.Add(100));
        Assert.Equal(0, wheel.Add(-200));
        Assert.Equal(-1, wheel.Add(-20));
    }

    [Fact]
    public void ScrollSendsHiResAlwaysAndLegacyOnlyOnANotch()
    {
        var wheel = default(Wheel);
        var events = new InputEvent[2];

        Assert.Equal(1, LinuxInput.WheelEvents(ref wheel, 0x0B, 0x08, 60, events));
        Assert.Equal(Uinput.EvRel, events[0].Type);
        Assert.Equal((ushort)0x0B, events[0].Code);
        Assert.Equal(60, events[0].Value);

        Assert.Equal(2, LinuxInput.WheelEvents(ref wheel, 0x0B, 0x08, 60, events));
        Assert.Equal((ushort)0x08, events[1].Code);
        Assert.Equal(1, events[1].Value);

        Assert.Equal(0, LinuxInput.WheelEvents(ref wheel, 0x0B, 0x08, 0, events));
    }
}
