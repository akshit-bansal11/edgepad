using Edgepad.Input;
using Xunit;

namespace Edgepad.Tests;

public sealed class KeyMapTests
{
    // Windows virtual-key code, then the evdev KEY_* value from linux/input-event-codes.h.
    [Theory]
    [InlineData(0x41, 30)] // A
    [InlineData(0x51, 16)] // Q
    [InlineData(0x5A, 44)] // Z
    [InlineData(0x30, 11)] // 0
    [InlineData(0x31, 2)] // 1
    [InlineData(0x39, 10)] // 9
    [InlineData(0x70, 59)] // F1
    [InlineData(0x79, 68)] // F10
    [InlineData(0x7A, 87)] // F11
    [InlineData(0x7B, 88)] // F12
    [InlineData(0x7C, 183)] // F13
    [InlineData(0x87, 194)] // F24
    [InlineData(0x25, 105)] // Left
    [InlineData(0x26, 103)] // Up
    [InlineData(0x27, 106)] // Right
    [InlineData(0x28, 108)] // Down
    [InlineData(0x10, 42)] // Shift
    [InlineData(0xA1, 54)] // right Shift
    [InlineData(0x11, 29)] // Ctrl
    [InlineData(0xA3, 97)] // right Ctrl
    [InlineData(0x12, 56)] // Alt
    [InlineData(0xA5, 100)] // right Alt (AltGr)
    [InlineData(0x5B, 125)] // left Win -> KEY_LEFTMETA
    [InlineData(0x5C, 126)] // right Win -> KEY_RIGHTMETA
    [InlineData(0x5D, 127)] // Menu -> KEY_COMPOSE
    [InlineData(0x2C, 99)] // PrtScn -> KEY_SYSRQ
    [InlineData(0x0D, 28)] // Enter
    [InlineData(0x08, 14)] // Backspace
    [InlineData(0x1B, 1)] // Esc
    [InlineData(0x60, 82)] // numpad 0
    [InlineData(0x67, 71)] // numpad 7
    [InlineData(0xB3, 164)] // play/pause
    [InlineData(0xAD, 113)] // mute
    [InlineData(0xBA, 39)] // ;
    [InlineData(0xDE, 40)] // '
    public void MapsByPosition(int code, int key) => Assert.Equal((ushort)key, KeyMap.ToEvdev((ushort)code));

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x07)]
    [InlineData(0xFF)]
    [InlineData(0x1234)]
    public void AnUnknownCodeHasNoKey(int code) => Assert.Equal(0, KeyMap.ToEvdev((ushort)code));

    [Fact]
    public void EveryKeyIsDeclaredOnceAndIsReal()
    {
        Assert.Equal(KeyMap.AllKeys.Count, KeyMap.AllKeys.Distinct().Count());
        Assert.DoesNotContain((ushort)0, KeyMap.AllKeys);
        // KEY_MAX is 0x2ff; buttons start at 0x100, and the keyboard must declare none of them.
        Assert.All(KeyMap.AllKeys, key => Assert.InRange(key, (ushort)1, (ushort)0xFF));
    }
}
