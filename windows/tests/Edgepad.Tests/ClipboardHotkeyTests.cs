using Edgepad.Blob;
using Edgepad.Injection;
using Xunit;

namespace Edgepad.Tests;

// Registration itself needs a desktop, so it is not tested here.
public sealed class ClipboardHotkeyTests
{
    [Fact]
    public void DisplayNamesTheShortcut() => Assert.Equal("Win+Alt+V", ClipboardHotkey.Display);

    [Fact]
    public void PasteIsCtrlDownVDownVUpCtrlUp()
    {
        var batch = PasteKeys.CtrlV();

        Assert.Equal(new ushort[] { 0x11, 0x56, 0x56, 0x11 }, batch.Select(i => i.Data.Keyboard.VirtualKey));
        Assert.Equal(new[] { false, false, true, true }, batch.Select(i => (i.Data.Keyboard.Flags & NativeMethods.KeyUp) != 0));
    }
}
