using Edgepad.Blob;
using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class CaptureFoldersTests
{
    private static LinuxCaptureFolders With(Func<byte[]?> fingerprint, Func<ClipboardContent?> read, bool available = true) =>
        new(fingerprint, read, () => available);

    [Fact]
    public void TheStampChangesWhenTheFingerprintDoes()
    {
        byte[]? print = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        var folders = With(() => print, () => null);

        var before = folders.ClipboardStamp();
        print = [9, 8, 7, 6, 5, 4, 3, 2, 1];

        Assert.NotEqual(before, folders.ClipboardStamp());
        Assert.NotEqual(0L, folders.ClipboardStamp());
    }

    [Fact]
    public void WithoutAClipboardToolThereIsNoStamp() =>
        Assert.Null(With(() => [1, 2, 3, 4, 5, 6, 7, 8], () => null, available: false).ClipboardStamp());

    [Fact]
    public void AnEmptyClipboardStampsZero() => Assert.Equal(0L, With(() => null, () => null).ClipboardStamp());

    [Fact]
    public void OnlyAnImageIsReturned()
    {
        byte[] png = [1, 2, 3];

        Assert.Null(With(() => null, () => new ClipboardContent(BlobKind.ClipText, png)).ClipboardPng());
        Assert.Null(With(() => null, () => new ClipboardContent(BlobKind.ClipText, [], Sensitive: true)).ClipboardPng());
        Assert.Null(With(() => null, () => null).ClipboardPng());
        Assert.Equal(png, With(() => null, () => new ClipboardContent(BlobKind.ClipImage, png)).ClipboardPng());
    }
}
