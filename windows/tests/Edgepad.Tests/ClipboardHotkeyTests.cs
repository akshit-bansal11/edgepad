using Edgepad.Blob;
using Edgepad.Injection;
using Xunit;

namespace Edgepad.Tests;

// Registration itself needs a desktop, so it is not tested here.
public sealed class ClipboardHotkeyTests
{
    private static readonly ushort[] CtrlVKeys = [0x11, 0x56, 0x56, 0x11];
    private static readonly bool[] CtrlVUps = [false, false, true, true];

    [Fact]
    public void DisplayNamesTheShortcut() => Assert.Equal("Win+Alt+V", ClipboardHotkey.Display);

    [Fact]
    public void PasteIsCtrlDownVDownVUpCtrlUp()
    {
        var batch = PasteKeys.CtrlV();

        Assert.Equal(CtrlVKeys, batch.Select(i => i.Data.Keyboard.VirtualKey));
        Assert.Equal(CtrlVUps, batch.Select(i => (i.Data.Keyboard.Flags & NativeMethods.KeyUp) != 0));
    }
}
