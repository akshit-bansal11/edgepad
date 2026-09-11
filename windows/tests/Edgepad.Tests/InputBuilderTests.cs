using System.Runtime.InteropServices;
using Edgepad.Injection;
using Xunit;

namespace Edgepad.Tests;

public sealed class InputBuilderTests
{
    [Fact]
    public void NativeInputHasTheWin32Size() =>
        Assert.Equal(Environment.Is64BitProcess ? 40 : 28, Marshal.SizeOf<NativeInput>());

    [Fact]
    public void AChordPressesInOrderAndReleasesInReverse()
    {
        var batch = InputBuilder.Chord(Keys.LWin, Keys.D);

        var expected = new (Keys Key, bool Up)[] { (Keys.LWin, false), (Keys.D, false), (Keys.D, true), (Keys.LWin, true) };
        Assert.Equal(expected, batch.Select(i => ((Keys)i.Data.Keyboard.VirtualKey, (i.Data.Keyboard.Flags & NativeMethods.KeyUp) != 0)));
    }

    [Fact]
    public void TheWindowsKeyIsAnExtendedKeyAndALetterIsNot()
    {
        Assert.NotEqual(0u, InputBuilder.Key(Keys.LWin, up: false).Data.Keyboard.Flags & NativeMethods.KeyExtended);
        Assert.Equal(0u, InputBuilder.Key(Keys.D, up: false).Data.Keyboard.Flags & NativeMethods.KeyExtended);
    }

    [Fact]
    public void ZoomWrapsOneWheelEventInControl()
    {
        var batch = InputBuilder.Zoom(120);

        Assert.Equal(3, batch.Length);
        Assert.Equal((ushort)Keys.ControlKey, batch[0].Data.Keyboard.VirtualKey);
        Assert.Equal(NativeMethods.MouseWheel, batch[1].Data.Mouse.Flags);
        Assert.Equal(120, batch[1].Data.Mouse.MouseData);
        Assert.Equal((ushort)Keys.ControlKey, batch[2].Data.Keyboard.VirtualKey);
        Assert.NotEqual(0u, batch[2].Data.Keyboard.Flags & NativeMethods.KeyUp);
    }

    [Fact]
    public void ScrollSendsOnlyTheAxesThatMoved()
    {
        Assert.Empty(InputBuilder.Scroll(0, 0));
        Assert.Equal(NativeMethods.MouseWheel, Assert.Single(InputBuilder.Scroll(0, -120)).Data.Mouse.Flags);
        Assert.Equal(NativeMethods.MouseHorizontalWheel, Assert.Single(InputBuilder.Scroll(40, 0)).Data.Mouse.Flags);
        Assert.Equal(2, InputBuilder.Scroll(40, -120).Length);
    }

    [Fact]
    public void ACharacterIsAUnicodeKeyDownThenUp()
    {
        var batch = InputBuilder.Unicode('é');

        Assert.Equal(2, batch.Length);
        Assert.Equal((ushort)'é', batch[0].Data.Keyboard.ScanCode);
        Assert.Equal(NativeMethods.KeyUnicode, batch[0].Data.Keyboard.Flags);
        Assert.Equal(NativeMethods.KeyUnicode | NativeMethods.KeyUp, batch[1].Data.Keyboard.Flags);
    }

    [Fact]
    public void AnUnknownMouseButtonIsRejected() =>
        Assert.Throws<InvalidDataException>(() => InputBuilder.ButtonFlags(3));
}
