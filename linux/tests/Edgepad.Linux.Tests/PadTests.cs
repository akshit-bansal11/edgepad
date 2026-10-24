using Edgepad.Input;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class PadTests
{
    // linux/input-event-codes.h, written out here rather than read from the code under test.
    private const ushort AbsX = 0x00;
    private const ushort AbsY = 0x01;
    private const ushort AbsZ = 0x02;
    private const ushort AbsRx = 0x03;
    private const ushort AbsRy = 0x04;
    private const ushort AbsRz = 0x05;
    private const ushort HatX = 0x10;
    private const ushort HatY = 0x11;

    private static readonly ushort[] ButtonCodes =
        [0x130, 0x131, 0x133, 0x134, 0x136, 0x137, 0x13A, 0x13B, 0x13C, 0x13D, 0x13E];

    private static InputEvent[] Events(PadState state)
    {
        var events = new InputEvent[UinputPad.EventCount];
        UinputPad.Fill(state, events);
        return events;
    }

    private static PadState Buttons(ushort mask) => new(mask, 0, 0, 0, 0, 0, 0);

    private static int Value(InputEvent[] events, ushort type, ushort code) =>
        Assert.Single(events, e => e.Type == type && e.Code == code).Value;

    [Theory]
    [InlineData(0x1000, 0x130)] // A: BTN_A
    [InlineData(0x2000, 0x131)] // B: BTN_B
    [InlineData(0x4000, 0x133)] // X: BTN_X, as xpad reports it, not BTN_WEST
    [InlineData(0x8000, 0x134)] // Y: BTN_Y, as xpad reports it, not BTN_NORTH
    [InlineData(0x0100, 0x136)] // LEFT_SHOULDER: BTN_TL
    [InlineData(0x0200, 0x137)] // RIGHT_SHOULDER: BTN_TR
    [InlineData(0x0020, 0x13A)] // BACK: BTN_SELECT
    [InlineData(0x0010, 0x13B)] // START: BTN_START
    [InlineData(0x0400, 0x13C)] // GUIDE: BTN_MODE
    [InlineData(0x0040, 0x13D)] // LEFT_THUMB: BTN_THUMBL
    [InlineData(0x0080, 0x13E)] // RIGHT_THUMB: BTN_THUMBR
    public void EachMaskBitPressesItsOneButton(ushort mask, ushort code)
    {
        var events = Events(Buttons(mask));
        foreach (var button in ButtonCodes)
        {
            Assert.Equal(button == code ? 1 : 0, Value(events, Uinput.EvKey, button));
        }

        Assert.Equal(0, Value(events, Uinput.EvAbs, HatX));
        Assert.Equal(0, Value(events, Uinput.EvAbs, HatY));
    }

    [Fact]
    public void ACombinedMaskPressesEachOfItsButtons()
    {
        // A, LEFT_SHOULDER, START, DPAD_UP and DPAD_RIGHT.
        var events = Events(Buttons(0x1000 | 0x0100 | 0x0010 | 0x0001 | 0x0008));
        ushort[] pressed = [0x130, 0x136, 0x13B];
        foreach (var button in ButtonCodes)
        {
            Assert.Equal(pressed.Contains(button) ? 1 : 0, Value(events, Uinput.EvKey, button));
        }

        Assert.Equal(1, Value(events, Uinput.EvAbs, HatX));
        Assert.Equal(-1, Value(events, Uinput.EvAbs, HatY));
    }

    [Fact]
    public void TheUnusedBitPressesNothing()
    {
        var events = Events(Buttons(0x0800));
        Assert.All(events.Where(e => e.Type == Uinput.EvKey), e => Assert.Equal(0, e.Value));
    }

    [Fact]
    public void AFrameIsEveryValueThenOneReport()
    {
        var events = Events(Buttons(0));
        Assert.Equal(20, events.Length);
        Assert.Equal(11, events.Count(e => e.Type == Uinput.EvKey));
        Assert.Equal(8, events.Count(e => e.Type == Uinput.EvAbs));
        Assert.Equal(Uinput.EvSyn, events[^1].Type);
        Assert.Equal(Uinput.SynReport, events[^1].Code);
    }

    [Theory]
    [InlineData(1, 0, 0)] // up
    [InlineData(2, 0, 1)] // down: evdev's hat is positive down
    [InlineData(4, -1, 0)] // left
    [InlineData(8, 1, 0)] // right
    [InlineData(5, -1, -1)] // up and left
    [InlineData(10, 1, 1)] // down and right
    [InlineData(3, 0, 0)] // up and down together cancel, as xpad's down-minus-up does
    [InlineData(12, 0, 0)] // left and right together cancel
    [InlineData(15, 0, 0)] // all four
    public void TheDpadIsTheHat(ushort mask, int x, int y)
    {
        var events = Events(Buttons(mask));
        Assert.Equal(x, Value(events, Uinput.EvAbs, HatX));
        Assert.Equal(y, Value(events, Uinput.EvAbs, HatY));
    }

    [Theory]
    [InlineData(32767, 32767, -32768)]
    [InlineData(-32768, -32768, 32767)]
    [InlineData(0, 0, -1)] // xpad's bitwise NOT puts a centred Y at -1, inside the flat
    [InlineData(1000, 1000, -1001)]
    public void SticksKeepXAndFlipYAsXpadDoes(short value, int x, int y)
    {
        var events = Events(new PadState(0, 0, 0, value, value, value, value));
        Assert.Equal(x, Value(events, Uinput.EvAbs, AbsX));
        Assert.Equal(y, Value(events, Uinput.EvAbs, AbsY));
        Assert.Equal(x, Value(events, Uinput.EvAbs, AbsRx));
        Assert.Equal(y, Value(events, Uinput.EvAbs, AbsRy));
    }

    [Fact]
    public void AStickPushedUpReadsNegativeY()
    {
        // XInput's up is positive; evdev's up is negative.
        var events = Events(new PadState(0, 0, 0, 0, short.MaxValue, 0, short.MinValue));
        Assert.True(Value(events, Uinput.EvAbs, AbsY) < 0);
        Assert.True(Value(events, Uinput.EvAbs, AbsRy) > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(255)]
    public void TriggersGoAcrossUnchanged(byte value)
    {
        var events = Events(new PadState(0, value, (byte)(255 - value), 0, 0, 0, 0));
        Assert.Equal((int)value, Value(events, Uinput.EvAbs, AbsZ));
        Assert.Equal(255 - value, Value(events, Uinput.EvAbs, AbsRz));
    }

    [Fact]
    public void NoUinputIsNoDriver()
    {
        using var pad = new LinuxPad("/nonexistent/uinput");
        Assert.Equal(PadStatus.NoDriver, pad.Probe());
        Assert.Equal(PadStatus.NoDriver, pad.Attach());
        Assert.False(pad.Update(Buttons(0x1000)));
        Assert.Equal(PadStatus.NoDriver, pad.Detach());
    }

    [Fact]
    public void ANodeThatRefusesTheIoctlsIsAttachFailed()
    {
        // /dev/null opens like /dev/uinput and answers every ioctl with ENOTTY.
        using var pad = new LinuxPad("/dev/null");
        Assert.Equal(PadStatus.Ready, pad.Probe());
        Assert.Equal(PadStatus.AttachFailed, pad.Attach());
        Assert.False(pad.Update(Buttons(0x1000)));
        Assert.Equal(PadStatus.Ready, pad.Detach());
    }
}
