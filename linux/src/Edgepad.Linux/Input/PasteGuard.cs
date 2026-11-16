using Edgepad.Blob;
using Edgepad.Protocol;

namespace Edgepad.Input;

/// <summary>
/// Keeps the user's clipboard across text typed by pasting it: what the clipboard held is read first and written back
/// after the paste. A clipboard tool that fails never stops the typing; it only means nothing is restored. A sensitive
/// clipboard (a password manager's hint) is never read, so it cannot be restored and the pasted text replaces it. Only
/// what <see cref="IClipboardSurface.Read"/> can hand back survives: one text or PNG, not every format the app offered.
/// </summary>
internal static class PasteGuard
{
    private static int writeFailureLogged;

    /// <param name="surface">The clipboard to save and restore.</param>
    /// <param name="paste">Puts text on the clipboard and pastes it. Its exceptions propagate, after the restore.</param>
    /// <param name="settle">Waits for the target app to take the selection, since restoring at once would paste the old content.</param>
    internal static void Around(IClipboardSurface surface, Action paste, Action settle)
    {
        ClipboardContent? before;
        try
        {
            before = surface.Read();
        }
        catch (Exception e)
        {
            Log.Write($"Clipboard could not be saved before a paste: {e.GetType().Name}: {e.Message}");
            before = null;
        }

        try
        {
            paste();
            settle();
        }
        finally
        {
            if (Restorable(before))
            {
                try
                {
                    surface.Write(before);
                }
                catch (Exception e)
                {
                    if (Interlocked.Exchange(ref writeFailureLogged, 1) == 0)
                    {
                        Log.Write($"Clipboard could not be restored after a paste: {e.GetType().Name}: {e.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Content worth writing back. Over the cap, a read hands back a cut-off copy, and writing that back would corrupt
    /// it, so the pasted text stays instead.
    /// </summary>
    private static bool Restorable([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] ClipboardContent? content) =>
        content is { Sensitive: false, Data.Length: > 0 }
        && content.Data.Length <= (content.Kind == BlobKind.ClipImage ? ClipboardSync.MaxImageBytes : ClipboardSync.MaxTextBytes);
}
