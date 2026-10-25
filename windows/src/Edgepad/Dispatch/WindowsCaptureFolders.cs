using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Edgepad.Capture;
using Edgepad.Protocol;

namespace Edgepad.Dispatch;

/// <summary>
/// Where Windows 11's Snipping Tool saves: screenshots autosave to Pictures\Screenshots and recordings to
/// Videos\Screen Recordings, both under the folders Windows itself names (so a Pictures folder OneDrive moved is
/// still found). A shot with autosave off is only on the clipboard, which is read as the fallback.
/// </summary>
internal sealed partial class WindowsCaptureFolders : ICaptureFolders
{
    public IReadOnlyList<CaptureFolder> For(ActionId capture) => capture == ActionId.CaptureRecord
        ? [new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screen Recordings"), Shared: false)]
        : [new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"), Shared: false)];

    /// <summary>Win+Shift+R opens the Snipping Tool's recorder, which is stopped in its own window, not by the chord.</summary>
    public bool RecordToggles => false;

    public long? ClipboardStamp() => GetClipboardSequenceNumber();

    /// <summary>The clipboard is an STA's to read, which a watch's pool thread is not, so it gets one of its own.</summary>
    public byte[]? ClipboardPng()
    {
        byte[]? png = null;
        var reader = new Thread(() =>
        {
            try
            {
                if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
                {
                    using (image)
                    {
                        using var stream = new MemoryStream();
                        image.Save(stream, ImageFormat.Png);
                        png = stream.ToArray();
                    }
                }
            }
            catch (Exception e) when (e is ExternalException or ThreadStateException)
            {
                // Another app holds the clipboard: nothing to send, and the shot stays where it is.
            }
        });
        reader.SetApartmentState(ApartmentState.STA);
        reader.Start();
        reader.Join();
        return png;
    }

    /// <summary>Changes whenever the clipboard does; 0 when this session cannot see it.</summary>
    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();
}
