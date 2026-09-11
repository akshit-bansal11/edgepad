using static Edgepad.Injection.NativeMethods;

namespace Edgepad.Injection;

/// <summary>
/// Builds SendInput batches. Pure, so the ordering rules can be tested without moving the real cursor.
/// Key codes come from WinForms' <see cref="Keys"/>, whose values are the Win32 virtual-key codes.
/// </summary>
internal static class InputBuilder
{
    public static NativeInput Mouse(uint flags, int dx = 0, int dy = 0, int data = 0) => new()
    {
        Type = InputMouse,
        Data = new NativeInputData { Mouse = new MouseInput { Dx = dx, Dy = dy, MouseData = data, Flags = flags } },
    };

    public static NativeInput Key(Keys key, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new NativeInputData
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = (ushort)key,
                Flags = (up ? KeyUp : 0) | (IsExtended(key) ? KeyExtended : 0),
            },
        },
    };

    /// <summary>
    /// A key by raw Windows virtual-key code (the phone sends KEY frames this way, not through <see cref="Keys"/>).
    /// Scan code always comes from <see cref="MapVirtualKeyW"/> so games and apps that read scan codes work.
    /// </summary>
    public static NativeInput VirtualKey(ushort code, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new NativeInputData
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = code,
                ScanCode = (ushort)MapVirtualKeyW(code, 0),
                Flags = (up ? KeyUp : 0) | (IsExtendedCode(code) ? KeyExtended : 0),
            },
        },
    };

    /// <summary>One character as Windows' Unicode key event, down then up, whatever the keyboard layout.</summary>
    public static NativeInput[] Unicode(char c) => [UnicodeKey(c, up: false), UnicodeKey(c, up: true)];

    private static NativeInput UnicodeKey(char c, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new NativeInputData
        {
            Keyboard = new KeyboardInput { ScanCode = c, Flags = KeyUnicode | (up ? KeyUp : 0) },
        },
    };

    /// <summary>Presses every key in order, then releases them in reverse, as one batch so nothing interleaves.</summary>
    public static NativeInput[] Chord(params ReadOnlySpan<Keys> keys)
    {
        var batch = new NativeInput[keys.Length * 2];
        for (var i = 0; i < keys.Length; i++)
        {
            batch[i] = Key(keys[i], up: false);
            batch[^(i + 1)] = Key(keys[i], up: true);
        }

        return batch;
    }

    /// <summary>Scrolling both axes in one batch; an axis at zero sends nothing.</summary>
    public static NativeInput[] Scroll(int dx, int dy) => (dx, dy) switch
    {
        (0, 0) => [],
        (0, _) => [Mouse(MouseWheel, data: dy)],
        (_, 0) => [Mouse(MouseHorizontalWheel, data: dx)],
        _ => [Mouse(MouseWheel, data: dy), Mouse(MouseHorizontalWheel, data: dx)],
    };

    /// <summary>Pinch zoom is Ctrl+wheel, which is what a precision touchpad's pinch produces in most apps.</summary>
    public static NativeInput[] Zoom(int delta) =>
        [Key(Keys.ControlKey, up: false), Mouse(MouseWheel, data: delta), Key(Keys.ControlKey, up: true)];

    /// <summary>Down and up flags for a protocol button id: 0 left, 1 right, 2 middle.</summary>
    public static (uint Down, uint Up) ButtonFlags(byte id) => id switch
    {
        0 => (MouseLeftDown, MouseLeftUp),
        1 => (MouseRightDown, MouseRightUp),
        2 => (MouseMiddleDown, MouseMiddleUp),
        _ => throw new InvalidDataException($"No mouse button {id}"),
    };

    // Arrow keys and the Windows key sit in the extended key block; without the flag some apps read
    // an injected arrow as a numpad key.
    private static bool IsExtended(Keys key) => key is Keys.Left or Keys.Right or Keys.LWin;

    // Same block, by raw virtual-key code: arrows, Home/End/PageUp/PageDown, Insert/Delete, both Win keys,
    // right Ctrl/Alt, numpad divide, NumLock and PrintScreen.
    private static readonly HashSet<ushort> ExtendedCodes =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2C, 0x2D, 0x2E, 0x5B, 0x5C, 0x6F, 0x90, 0xA3, 0xA5,
    ];

    private static bool IsExtendedCode(ushort code) => ExtendedCodes.Contains(code);
}
