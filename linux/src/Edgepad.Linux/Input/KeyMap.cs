namespace Edgepad.Input;

/// <summary>
/// What a Windows virtual-key code, the protocol's one key namespace, means as an evdev <c>KEY_*</c> code
/// (linux/input-event-codes.h). By position, as the Mac's KeyMap does: a code names the key in that place on a US
/// PC keyboard, and the desktop's own layout decides what character it makes. Win is the left Meta key and Menu
/// (VK_APPS) is KEY_COMPOSE, the code a PC keyboard's menu key sends.
/// </summary>
internal static class KeyMap
{
    public const ushort LeftShift = 42;
    public const ushort LeftCtrl = 29;
    public const ushort Backspace = 14;
    public const ushort Enter = 28;
    public const ushort Tab = 15;
    public const ushort Space = 57;
    public const ushort V = 47;

    // Above AllKeys, which reads them: static fields are initialised in the order they are written.
    // KEY_A..KEY_Z follow the keyboard's rows, not the alphabet.
    private static readonly ushort[] Letters =
        [30, 48, 46, 32, 18, 33, 34, 35, 23, 36, 37, 38, 50, 49, 24, 25, 16, 19, 31, 20, 22, 47, 17, 45, 21, 44];

    // KEY_KP0..KEY_KP9: the numpad's rows, 7-8-9 at the top.
    private static readonly ushort[] Numpad = [82, 79, 80, 81, 75, 76, 77, 71, 72, 73];

    /// <summary>Every evdev code <see cref="ToEvdev"/> can answer, which the virtual keyboard must declare up front.</summary>
    public static IReadOnlyList<ushort> AllKeys { get; } =
        [.. Enumerable.Range(0, 256).Select(code => ToEvdev((ushort)code)).Where(key => key != 0).Distinct()];

    /// <summary>The evdev key, or 0 (KEY_RESERVED) for a code this laptop has no key for.</summary>
    public static ushort ToEvdev(ushort code) => (ushort)(code switch
    {
        0x08 => Backspace,
        0x09 => Tab,
        0x0D => Enter,
        0x10 or 0xA0 => LeftShift,
        0xA1 => 54, // KEY_RIGHTSHIFT
        0x11 or 0xA2 => LeftCtrl,
        0xA3 => 97, // KEY_RIGHTCTRL
        0x12 or 0xA4 => 56, // KEY_LEFTALT
        0xA5 => 100, // KEY_RIGHTALT
        0x13 => 119, // KEY_PAUSE
        0x14 => 58, // KEY_CAPSLOCK
        0x1B => 1, // KEY_ESC
        0x20 => Space,
        0x21 => 104, // KEY_PAGEUP
        0x22 => 109, // KEY_PAGEDOWN
        0x23 => 107, // KEY_END
        0x24 => 102, // KEY_HOME
        0x25 => 105, // KEY_LEFT
        0x26 => 103, // KEY_UP
        0x27 => 106, // KEY_RIGHT
        0x28 => 108, // KEY_DOWN
        0x2C => 99, // KEY_SYSRQ: what a PC keyboard's PrtSc sends; desktops bind Print to it
        0x2D => 110, // KEY_INSERT
        0x2E => 111, // KEY_DELETE
        0x2F => 138, // KEY_HELP
        0x30 => 11, // KEY_0; KEY_1..KEY_9 are 2..10
        >= 0x31 and <= 0x39 => code - 0x31 + 2,
        >= 0x41 and <= 0x5A => Letters[code - 0x41],
        0x5B => 125, // KEY_LEFTMETA
        0x5C => 126, // KEY_RIGHTMETA
        0x5D => 127, // KEY_COMPOSE
        0x5F => 142, // KEY_SLEEP
        >= 0x60 and <= 0x69 => Numpad[code - 0x60],
        0x6A => 55, // KEY_KPASTERISK
        0x6B => 78, // KEY_KPPLUS
        0x6C => 121, // KEY_KPCOMMA
        0x6D => 74, // KEY_KPMINUS
        0x6E => 83, // KEY_KPDOT
        0x6F => 98, // KEY_KPSLASH
        >= 0x70 and <= 0x79 => code - 0x70 + 59, // KEY_F1..KEY_F10 are 59..68
        0x7A => 87, // KEY_F11
        0x7B => 88, // KEY_F12
        >= 0x7C and <= 0x87 => code - 0x7C + 183, // KEY_F13..KEY_F24 are 183..194
        0x90 => 69, // KEY_NUMLOCK
        0x91 => 70, // KEY_SCROLLLOCK
        0xA6 => 158, // KEY_BACK
        0xA7 => 159, // KEY_FORWARD
        0xA8 => 173, // KEY_REFRESH
        0xA9 => 128, // KEY_STOP
        0xAA => 217, // KEY_SEARCH
        0xAB => 156, // KEY_BOOKMARKS
        0xAC => 172, // KEY_HOMEPAGE
        0xAD => 113, // KEY_MUTE
        0xAE => 114, // KEY_VOLUMEDOWN
        0xAF => 115, // KEY_VOLUMEUP
        0xB0 => 163, // KEY_NEXTSONG
        0xB1 => 165, // KEY_PREVIOUSSONG
        0xB2 => 166, // KEY_STOPCD
        0xB3 => 164, // KEY_PLAYPAUSE
        0xB4 => 155, // KEY_MAIL
        0xB5 => 226, // KEY_MEDIA
        0xB6 => 157, // KEY_COMPUTER
        0xB7 => 140, // KEY_CALC
        0xBA => 39, // KEY_SEMICOLON
        0xBB => 13, // KEY_EQUAL
        0xBC => 51, // KEY_COMMA
        0xBD => 12, // KEY_MINUS
        0xBE => 52, // KEY_DOT
        0xBF => 53, // KEY_SLASH
        0xC0 => 41, // KEY_GRAVE
        0xDB => 26, // KEY_LEFTBRACE
        0xDC => 43, // KEY_BACKSLASH
        0xDD => 27, // KEY_RIGHTBRACE
        0xDE => 40, // KEY_APOSTROPHE
        0xE2 => 86, // KEY_102ND: the extra key beside left Shift on ISO keyboards
        _ => 0,
    });
}
