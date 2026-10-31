using Edgepad.Input;
using Xunit;

namespace Edgepad.Tests;

public sealed class TypingTests
{
    private const ushort A = 30;
    private const ushort H = 35;
    private const ushort I = 23;
    private const ushort One = 2;
    private const ushort Slash = 53;

    [Fact]
    public void UsAsciiIsKeys() =>
        Plans("hi /", true, TypingStep.Press(H), TypingStep.Press(I), TypingStep.Press(KeyMap.Space), TypingStep.Press(Slash));

    [Fact]
    public void UppercaseAndShiftedSymbolsHoldShift() =>
        Plans("A!?", true, TypingStep.Press(A, shift: true), TypingStep.Press(One, shift: true), TypingStep.Press(Slash, shift: true));

    [Fact]
    public void BackspaceAndEnterAreKeys() =>
        Plans("a\b\n", true, TypingStep.Press(A), TypingStep.Press(KeyMap.Backspace), TypingStep.Press(KeyMap.Enter));

    [Fact]
    public void NonAsciiIsPasted() => Plans("é", true, TypingStep.PasteText("é"));

    [Fact]
    public void PastesAreJoinedBetweenKeys() =>
        Plans("aé😀ü\n", true, TypingStep.Press(A), TypingStep.PasteText("é😀ü"), TypingStep.Press(KeyMap.Enter));

    [Fact]
    public void OnAnotherLayoutEverythingButEditingKeysIsPasted() =>
        Plans(
            "Hi y\bz\n",
            false,
            TypingStep.PasteText("Hi y"),
            TypingStep.Press(KeyMap.Backspace),
            TypingStep.PasteText("z"),
            TypingStep.Press(KeyMap.Enter));

    [Fact]
    public void WaylandUsesWlCopy()
    {
        var tool = Typing.ClipboardFor("wayland-0", ":0");
        Assert.NotNull(tool);
        Assert.Equal("wl-copy", tool.Program);
        Assert.Empty(tool.Arguments);
    }

    [Fact]
    public void X11UsesXclipOnTheClipboardSelection()
    {
        var tool = Typing.ClipboardFor(null, ":0");
        Assert.NotNull(tool);
        Assert.Equal("xclip", tool.Program);
        string[] expected = ["-selection", "clipboard"];
        Assert.Equal(expected, tool.Arguments);
    }

    [Fact]
    public void NoDisplayNoTool()
    {
        Assert.Null(Typing.ClipboardFor(null, null));
        Assert.Null(Typing.ClipboardFor("", ""));
    }

    [Fact]
    public void PlainUsIsUs() =>
        Assert.True(Typing.IsUsLayout("   System Locale: LANG=en_US.UTF-8\n       VC Keymap: us\n      X11 Layout: us\n       X11 Model: pc105\n"));

    [Theory]
    [InlineData("      X11 Layout: de\n       X11 Model: pc105\n")]
    [InlineData("      X11 Layout: us\n     X11 Variant: dvorak\n")]
    [InlineData("      X11 Layout: us,de\n")]
    [InlineData("       VC Keymap: us\n")]
    [InlineData("")]
    public void AnythingElseIsNot(string status) => Assert.False(Typing.IsUsLayout(status));

    private static void Plans(string text, bool usLayout, params TypingStep[] expected) =>
        Assert.Equal(expected, Typing.Plan(text, usLayout));
}
