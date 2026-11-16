using Edgepad.Blob;
using Edgepad.Capture;
using Edgepad.Protocol;
using Edgepad.Ui;

namespace Edgepad.Platform;

/// <summary>
/// Where the desktop's own tools save. GNOME's screenshot UI saves to Screenshots in the Pictures folder and its
/// recorder to Screencasts in the Videos folder; KDE's Spectacle saves both into the Pictures and Videos folders
/// themselves. Both of each are watched, which is safe: a capture is one new file across all of them. A shot that only
/// went to the clipboard is found through the clipboard tools (<see cref="LinuxClipboard"/>): the stamp is the head of
/// the clipboard's fingerprint, so only a short read is repeated while a capture is waited for, and the image itself is
/// fetched once. A password manager's copy is never fetched (<see cref="LinuxClipboard.Pick"/>).
/// </summary>
internal sealed class LinuxCaptureFolders(Func<byte[]?> fingerprint, Func<ClipboardContent?> read, Func<bool> available) : ICaptureFolders
{
    public IReadOnlyList<CaptureFolder> For(ActionId capture)
    {
        var folder = capture == ActionId.CaptureRecord
            ? SignatureExport.UserFolder("VIDEOS", "Videos")
            : SignatureExport.UserFolder("PICTURES", "Pictures");
        var sub = capture == ActionId.CaptureRecord ? "Screencasts" : "Screenshots";
        return [new(Path.Combine(folder, sub), Shared: false), new(folder, Shared: true)];
    }

    /// <summary>GNOME's and KDE's recording shortcuts both start and stop a recording.</summary>
    public bool RecordToggles => true;

    /// <summary>Null with no clipboard tool; 0 for an empty or unshareable clipboard, so a copy later still changes it.</summary>
    public long? ClipboardStamp()
    {
        if (!available())
        {
            return null;
        }

        return fingerprint() is { Length: >= sizeof(long) } print ? BitConverter.ToInt64(print, 0) : 0;
    }

    public byte[]? ClipboardPng() => read() is { Kind: BlobKind.ClipImage, Sensitive: false } content ? content.Data : null;
}
