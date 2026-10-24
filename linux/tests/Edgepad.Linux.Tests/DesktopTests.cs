using Edgepad.Input;
using Edgepad.Platform;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class DesktopTests
{
    // evdev codes from linux/input-event-codes.h.
    private const ushort Meta = 125;
    private const ushort Ctrl = 29;
    private const ushort Alt = 56;
    private const ushort Shift = 42;
    private const ushort Print = 99;

    [Theory]
    [InlineData("GNOME", "Gnome")]
    [InlineData("ubuntu:GNOME", "Gnome")]
    [InlineData("gnome-classic:GNOME", "Gnome")]
    [InlineData("KDE", "Kde")]
    [InlineData("kde", "Kde")]
    [InlineData("X-Cinnamon", "Unknown")]
    [InlineData("XFCE", "Unknown")]
    [InlineData("", "Unknown")]
    [InlineData(null, "Unknown")]
    public void TheDesktopIsReadFromTheList(string? variable, string expected) =>
        Assert.Equal(Enum.Parse<DesktopEnvironment>(expected), Desktop.Detect(variable));

    [Fact]
    public void GnomeChords()
    {
        Expect(DesktopEnvironment.Gnome, ActionId.TaskView, Meta);
        Expect(DesktopEnvironment.Gnome, ActionId.ShowDesktop, Meta, 32); // D
        Expect(DesktopEnvironment.Gnome, ActionId.Search, Meta, 30); // A
        Expect(DesktopEnvironment.Gnome, ActionId.Notifications, Meta, 47); // V
        Expect(DesktopEnvironment.Gnome, ActionId.DesktopLeft, Meta, 104); // PageUp
        Expect(DesktopEnvironment.Gnome, ActionId.DesktopRight, Meta, 109); // PageDown
    }

    [Fact]
    public void KdeChords()
    {
        Expect(DesktopEnvironment.Kde, ActionId.TaskView, Meta, 17); // W
        Expect(DesktopEnvironment.Kde, ActionId.ShowDesktop, Meta, 32); // D
        Expect(DesktopEnvironment.Kde, ActionId.Search, Alt, 57); // Space
        Expect(DesktopEnvironment.Kde, ActionId.Notifications, Meta, 49); // N
        Expect(DesktopEnvironment.Kde, ActionId.DesktopLeft, Ctrl, Meta, 105);
        Expect(DesktopEnvironment.Kde, ActionId.DesktopRight, Ctrl, Meta, 106);
    }

    [Theory]
    [InlineData("Gnome")]
    [InlineData("Kde")]
    [InlineData("Unknown")]
    public void ScreenshotsPressPrintOnEveryDesktop(string name)
    {
        var desktop = Enum.Parse<DesktopEnvironment>(name);
        Expect(desktop, ActionId.CaptureRegion, Print);
        Expect(desktop, ActionId.CaptureScreen, Print);
    }

    [Fact]
    public void RecordingIsPerDesktopAndDroppedElsewhere()
    {
        Expect(DesktopEnvironment.Gnome, ActionId.CaptureRecord, Ctrl, Shift, Alt, 19); // R
        Expect(DesktopEnvironment.Kde, ActionId.CaptureRecord, Meta, Alt, 19);
        Assert.Null(Desktop.Chord(DesktopEnvironment.Unknown, ActionId.CaptureRecord));
    }

    [Theory]
    [InlineData((byte)ActionId.TaskView)]
    [InlineData((byte)ActionId.ShowDesktop)]
    [InlineData((byte)ActionId.Search)]
    [InlineData((byte)ActionId.Notifications)]
    [InlineData((byte)ActionId.DesktopLeft)]
    [InlineData((byte)ActionId.DesktopRight)]
    public void AnUnknownDesktopDropsTheDesktopActions(byte id) =>
        Assert.Null(Desktop.Chord(DesktopEnvironment.Unknown, (ActionId)id));

    [Theory]
    [InlineData("Gnome")]
    [InlineData("Kde")]
    public void EveryChordKeyIsOneTheVirtualKeyboardDeclares(string name)
    {
        // A key the device never declared is silently ignored by the kernel, so a typo in the table would be a
        // chord that does nothing and no error.
        var desktop = Enum.Parse<DesktopEnvironment>(name);
        var declared = KeyMap.AllKeys.ToHashSet();
        foreach (var id in Enum.GetValues<ActionId>())
        {
            if (Desktop.Chord(desktop, id) is { } keys)
            {
                Assert.All(keys, key => Assert.Contains(key, declared));
            }
        }
    }

    private static void Expect(DesktopEnvironment desktop, ActionId id, params ushort[] keys) =>
        Assert.Equal(keys, Desktop.Chord(desktop, id));
}
