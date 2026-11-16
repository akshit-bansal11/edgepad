using System.Text;
using Edgepad.Blob;
using Edgepad.Dispatch;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The clipboard fallback for a shot with autosave off goes through the same read as clipboard sharing.</summary>
public sealed class WindowsCaptureFoldersTests
{
    [Fact]
    public void ASwitchedOffOrSensitiveClipboardGivesNothing()
    {
        // WindowsClipboard.Read answers null for both.
        Assert.Null(new WindowsCaptureFolders(() => null).ClipboardPng());
    }

    [Fact]
    public void TextOnTheClipboardIsNoShot()
    {
        var text = new ClipboardContent(BlobKind.ClipText, Encoding.UTF8.GetBytes("hello"));

        Assert.Null(new WindowsCaptureFolders(() => text).ClipboardPng());
    }

    [Fact]
    public void AnImageOnTheClipboardIsTheShot()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47];

        Assert.Equal(png, new WindowsCaptureFolders(() => new ClipboardContent(BlobKind.ClipImage, png)).ClipboardPng());
    }
}
