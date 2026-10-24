using Edgepad.Protocol;

namespace Edgepad.Input;

/// <summary>
/// What the virtual controller is to the kernel, and how a PAD_STATE becomes its events. It presents as a wired
/// Xbox 360 pad, read the way the kernel's own xpad driver reports a real one, because that is the pad SDL, Steam
/// and every game on Linux already have a mapping for: same ids, same axes and ranges, same signs, same buttons.
/// Line numbers below are drivers/input/joystick/xpad.c at torvalds/linux 5b0764a5.
/// </summary>
internal static class UinputPad
{
    /// <summary>Eleven buttons, eight axes and the report: every PAD_STATE is this many events.</summary>
    public const int EventCount = 20;

    private const string Name = "Edgepad virtual Xbox 360 controller";

    // linux/input-event-codes.h.
    private const ushort AbsX = 0x00;
    private const ushort AbsY = 0x01;
    private const ushort AbsZ = 0x02;
    private const ushort AbsRx = 0x03;
    private const ushort AbsRy = 0x04;
    private const ushort AbsRz = 0x05;
    private const ushort AbsHat0X = 0x10;
    private const ushort AbsHat0Y = 0x11;

    // XInput's wButtons D-pad bits (protocol/actions.txt), which become the hat rather than buttons.
    private const ushort DpadUp = 0x0001;
    private const ushort DpadDown = 0x0002;
    private const ushort DpadLeft = 0x0004;
    private const ushort DpadRight = 0x0008;

    /// <summary>
    /// The USB ids of Microsoft's wired 360 pad (xpad.c:123), on BUS_USB as xpad's pads are. The version is that
    /// pad's bcdDevice, 0x0114, which is what a real one carries into SDL's GUID; UNVERIFIED that every SDL mapping
    /// for the pad accepts it (docs/LINUX.md).
    /// </summary>
    private static readonly InputId Id = new(Uinput.BusUsb, 0x045E, 0x028E, 0x0114);

    /// <summary>
    /// XInput mask to evdev button, in xpad's order (xpad360_process_packet, xpad.c:914-928): wButtons' low byte is
    /// the packet's data[2] and its high byte data[3]. X is BTN_X and Y is BTN_Y, as xpad reports them
    /// (xpad.c:924-925), even though input-event-codes.h aliases those to BTN_NORTH and BTN_WEST, the opposite of
    /// where X and Y sit on the pad. SDL's mappings for this pad are written against xpad, so copying it is what
    /// keeps X as X in a game.
    /// </summary>
    internal static readonly (ushort Mask, ushort Code)[] Buttons =
    [
        (0x1000, 0x130), // A: BTN_A (BTN_SOUTH)
        (0x2000, 0x131), // B: BTN_B (BTN_EAST)
        (0x4000, 0x133), // X: BTN_X (BTN_NORTH)
        (0x8000, 0x134), // Y: BTN_Y (BTN_WEST)
        (0x0100, 0x136), // LEFT_SHOULDER: BTN_TL
        (0x0200, 0x137), // RIGHT_SHOULDER: BTN_TR
        (0x0020, 0x13A), // BACK: BTN_SELECT
        (0x0010, 0x13B), // START: BTN_START
        (0x0400, 0x13C), // GUIDE: BTN_MODE
        (0x0040, 0x13D), // LEFT_THUMB: BTN_THUMBL
        (0x0080, 0x13E), // RIGHT_THUMB: BTN_THUMBR
    ];

    /// <summary>
    /// Each axis with xpad's range, fuzz and flat (xpad_set_up_abs, xpad.c:1905-1926): sticks -32768..32767 with
    /// fuzz 16 and flat 128, a 360 pad's triggers 0..255, and the D-pad as a hat of -1..1. The hat and not four
    /// buttons because 045e:028e's entry carries no MAP_DPAD_TO_BUTTONS (xpad.c:123), so a real one reports the hat.
    /// </summary>
    private static readonly (ushort Code, int Minimum, int Maximum, int Fuzz, int Flat)[] Axes =
    [
        (AbsX, short.MinValue, short.MaxValue, 16, 128),
        (AbsY, short.MinValue, short.MaxValue, 16, 128),
        (AbsRx, short.MinValue, short.MaxValue, 16, 128),
        (AbsRy, short.MinValue, short.MaxValue, 16, 128),
        (AbsZ, 0, byte.MaxValue, 0, 0),
        (AbsRz, 0, byte.MaxValue, 0, 0),
        (AbsHat0X, -1, 1, 0, 0),
        (AbsHat0Y, -1, 1, 0, 0),
    ];

    /// <summary>Enables every button and axis and creates the device. False, errno set, at the first refusal.</summary>
    public static bool Create(Uinput device)
    {
        var ok = device.Enable(Uinput.SetEvBit, Uinput.EvKey) && device.Enable(Uinput.SetEvBit, Uinput.EvAbs);
        foreach (var (_, code) in Buttons)
        {
            ok = ok && device.Enable(Uinput.SetKeyBit, code);
        }

        foreach (var (code, minimum, maximum, fuzz, flat) in Axes)
        {
            ok = ok && device.Enable(Uinput.SetAbsBit, code) && device.SetUpAxis(code, minimum, maximum, fuzz, flat);
        }

        return ok && device.Create(Id, Name);
    }

    /// <summary>
    /// The whole pad as <see cref="EventCount"/> events ending in SYN_REPORT. Every value goes every frame: the
    /// input core already drops a key or axis event that repeats the value it holds, so sending only what changed
    /// would save nothing a game can see, and would need a copy of the last frame kept here to compare against.
    /// </summary>
    public static void Fill(PadState state, Span<InputEvent> into)
    {
        var i = 0;
        foreach (var (mask, code) in Buttons)
        {
            into[i++] = new InputEvent(Uinput.EvKey, code, (state.Buttons & mask) != 0 ? 1 : 0);
        }

        // XInput's Y is positive up and evdev's is positive down. xpad flips it with a bitwise NOT rather than a
        // negation (xpad.c:934-935 and 940-941), which maps -32768..32767 exactly onto 32767..-32768 with nothing
        // to clamp, at the cost of a resting stick reading -1, well inside the flat of 128.
        into[i++] = new InputEvent(Uinput.EvAbs, AbsX, state.Lx);
        into[i++] = new InputEvent(Uinput.EvAbs, AbsY, ~state.Ly);
        into[i++] = new InputEvent(Uinput.EvAbs, AbsRx, state.Rx);
        into[i++] = new InputEvent(Uinput.EvAbs, AbsRy, ~state.Ry);
        into[i++] = new InputEvent(Uinput.EvAbs, AbsZ, state.Lt);
        into[i++] = new InputEvent(Uinput.EvAbs, AbsRz, state.Rt);

        // Right minus left and down minus up, as xpad computes the hat (xpad.c:907-910): both directions of one axis
        // held at once cancel to centre, which is also what a game reading XInput would make of them.
        into[i++] = new InputEvent(Uinput.EvAbs, AbsHat0X, Held(state, DpadRight) - Held(state, DpadLeft));
        into[i++] = new InputEvent(Uinput.EvAbs, AbsHat0Y, Held(state, DpadDown) - Held(state, DpadUp));
        into[i] = new InputEvent(Uinput.EvSyn, Uinput.SynReport, 0);
    }

    private static int Held(PadState state, ushort mask) => (state.Buttons & mask) != 0 ? 1 : 0;
}
