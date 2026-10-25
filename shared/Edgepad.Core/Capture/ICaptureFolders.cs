using Edgepad.Protocol;

namespace Edgepad.Capture;

/// <summary>
/// A folder a capture tool saves into. <c>Shared</c> is a folder other things save into as well (a Mac's Desktop, a
/// Linux Pictures folder), where only a file named like a capture is taken; any new file in one of the tool's own
/// folders is.
/// </summary>
internal readonly record struct CaptureFolder(string Path, bool Shared);

/// <summary>Where one laptop's own capture tool puts what it captures. Only the platform knows; the watch only looks.</summary>
internal interface ICaptureFolders
{
    /// <summary>The folders a file from <paramref name="capture"/> may appear in. Missing ones are skipped.</summary>
    IReadOnlyList<CaptureFolder> For(ActionId capture);

    /// <summary>
    /// Whether the record shortcut is a toggle, pressed again to stop (GNOME, KDE). There a recording is sent only once
    /// the phone has pressed it again; elsewhere only after a long quiet spell of its file.
    /// </summary>
    bool RecordToggles { get; }

    /// <summary>Something that changes whenever the clipboard does, or null where the clipboard is not watched.</summary>
    long? ClipboardStamp();

    /// <summary>The image on the clipboard as PNG bytes, or null when there is none.</summary>
    byte[]? ClipboardPng();
}
