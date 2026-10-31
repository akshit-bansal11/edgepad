using System.Drawing;
using System.Text;
using Edgepad.Blob;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The decisions around the clipboard, none of which needs one: CI has no real clipboard worth trusting, so the calls
/// themselves, and Clipboard history's part in them, are not proved here. ClipboardSync's own tests cover the loops.
/// </summary>
public sealed class WindowsClipboardTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(dir, "clipboard.txt");

    private static bool Sensitive(params (string Name, uint? Value)[] offered) =>
        ClipboardFlags.IsSensitive(offered.ToDictionary(flag => flag.Name, flag => flag.Value));

    [Fact]
    public void NoFlagsIsShareable() => Assert.False(Sensitive());

    [Fact]
    public void AnOrdinaryCopyAllowedInHistoryIsShareable() =>
        Assert.False(Sensitive((ClipboardFlags.History, 1u), (ClipboardFlags.Cloud, 1u)));

    [Theory]
    [InlineData(ClipboardFlags.ExcludeFromMonitors)]
    [InlineData(ClipboardFlags.ViewerIgnore)]
    public void BeingOfferedAtAllIsEnough(string flag)
    {
        Assert.True(Sensitive((flag, null)));
        Assert.True(Sensitive((flag, 0u)));
        Assert.True(Sensitive((flag, 1u)));
    }

    [Theory]
    [InlineData(ClipboardFlags.History)]
    [InlineData(ClipboardFlags.Cloud)]
    public void ZeroOrUnreadableForbids(string flag)
    {
        Assert.True(Sensitive((flag, 0u)));
        Assert.True(Sensitive((flag, null)));
        Assert.False(Sensitive((flag, 1u)));
    }

    [Fact]
    public void APasswordManagersCopyStaysHere()
    {
        // What such an app sets: allowed nowhere, whichever of the flags a given one bothers with.
        Assert.True(Sensitive((ClipboardFlags.ExcludeFromMonitors, 0u), (ClipboardFlags.History, 0u), (ClipboardFlags.Cloud, 0u)));
        Assert.True(Sensitive((ClipboardFlags.History, 1u), (ClipboardFlags.Cloud, 0u)));
        Assert.True(Sensitive((ClipboardFlags.History, 0u), (ClipboardFlags.Cloud, 1u)));
    }

    [Fact]
    public void EveryFlagIsLookedFor()
    {
        Assert.Contains(ClipboardFlags.ExcludeFromMonitors, ClipboardFlags.Names);
        Assert.Contains(ClipboardFlags.ViewerIgnore, ClipboardFlags.Names);
        Assert.Contains(ClipboardFlags.History, ClipboardFlags.Names);
        Assert.Contains(ClipboardFlags.Cloud, ClipboardFlags.Names);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("héllo wörld")]
    [InlineData("🙂")]
    public void TextStopsAtItsTerminator(string text)
    {
        var block = Encoding.Unicode.GetBytes(text + "\0\0junk");

        Assert.Equal(Encoding.UTF8.GetBytes(text), WindowsClipboard.Utf8(block));
    }

    [Fact]
    public void AnEmptyBlockIsNoText() => Assert.Empty(WindowsClipboard.Utf8(Encoding.Unicode.GetBytes("\0")));

    [Fact]
    public void ABitmapBecomesAPngOfTheSameSize()
    {
        using var bitmap = new Bitmap(3, 2);
        bitmap.SetPixel(1, 1, Color.Red);

        var png = WindowsClipboard.Png(bitmap);

        Assert.Equal((byte)0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
        using var decoded = new Bitmap(new MemoryStream(png));
        Assert.Equal(3, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.Equal(Color.Red.ToArgb(), decoded.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void NoFileIsOn() => Assert.True(new ClipboardSetting(FilePath).Enabled);

    [Fact]
    public void OffIsRememberedAndOnAgainToo()
    {
        var setting = ClipboardSetting.ForCurrentUser(dir);

        Assert.True(setting.Set(false));
        Assert.False(setting.Enabled);
        Assert.False(new ClipboardSetting(FilePath).Enabled);

        Assert.True(setting.Set(true));
        Assert.True(new ClipboardSetting(FilePath).Enabled);
    }

    [Fact]
    public void AFileEdgepadDidNotWriteIsOff()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, "enabled=yes\n");

        Assert.False(new ClipboardSetting(FilePath).Enabled);
    }

    [Fact]
    public void AnUnreadableFileIsOff()
    {
        Directory.CreateDirectory(FilePath);

        Assert.False(new ClipboardSetting(FilePath).Enabled);
    }

    [Fact]
    public void TurningOffHoldsEvenWhenItCannotBeSaved()
    {
        // A file where the folder should be, so the setting reads as no file and cannot be written.
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "in-the-way"), "");
        var setting = new ClipboardSetting(Path.Combine(dir, "in-the-way", "clipboard.txt"));
        Assert.True(setting.Enabled);

        Assert.False(setting.Set(false));
        Assert.False(setting.Enabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
