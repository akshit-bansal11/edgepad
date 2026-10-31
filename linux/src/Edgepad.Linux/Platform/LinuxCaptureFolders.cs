using Edgepad.Capture;
using Edgepad.Protocol;
using Edgepad.Ui;

namespace Edgepad.Platform;

/// <summary>
/// Where the desktop's own tools save. GNOME's screenshot UI saves to Screenshots in the Pictures folder and its
/// recorder to Screencasts in the Videos folder; KDE's Spectacle saves both into the Pictures and Videos folders
/// themselves. Both of each are watched, which is safe: a capture is one new file across all of them. The clipboard is
/// not read on Linux, so a shot that only went there stays there.
/// </summary>
internal sealed class LinuxCaptureFolders : ICaptureFolders
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

    public long? ClipboardStamp() => null;

    public byte[]? ClipboardPng() => null;
}
