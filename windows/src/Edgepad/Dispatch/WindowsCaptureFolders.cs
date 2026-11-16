using System.Runtime.InteropServices;
using Edgepad.Blob;
using Edgepad.Capture;
using Edgepad.Protocol;

namespace Edgepad.Dispatch;

/// <summary>
/// Where Windows 11's Snipping Tool saves: screenshots autosave to Pictures\Screenshots and recordings to
/// Videos\Screen Recordings, both under the folders Windows itself names (so a Pictures folder OneDrive moved is
/// still found). A shot with autosave off is only on the clipboard, which is read as the fallback through
/// <c>readClipboard</c> (<see cref="WindowsClipboard.Read"/>), so the owner's Share clipboard switch and
/// the sensitive-format flags hold for it as they do for clipboard sharing.
/// </summary>
internal sealed partial class WindowsCaptureFolders(Func<ClipboardContent?> readClipboard) : ICaptureFolders
{
    public IReadOnlyList<CaptureFolder> For(ActionId capture) => capture == ActionId.CaptureRecord
        ? [new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screen Recordings"), Shared: false)]
        : [new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"), Shared: false)];

    /// <summary>Win+Shift+R opens the Snipping Tool's recorder, which is stopped in its own window, not by the chord.</summary>
    public bool RecordToggles => false;

    public long? ClipboardStamp() => GetClipboardSequenceNumber();

    /// <summary>The clipboard's image as a PNG; nothing when it holds text, is switched off, or holds a sensitive copy.</summary>
    public byte[]? ClipboardPng() => readClipboard() is { Kind: BlobKind.ClipImage } image ? image.Data : null;

    /// <summary>Changes whenever the clipboard does; 0 when this session cannot see it.</summary>
    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();
}
