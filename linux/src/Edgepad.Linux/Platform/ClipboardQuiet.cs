using Edgepad.Blob;

namespace Edgepad.Platform;

/// <summary>
/// The clipboard as the shared sync sees it: nothing while <see cref="ClipboardQuiet"/> is up, checked before and after
/// the read, so a send queued by a copy just before typing starts never reads and sends the typed text.
/// </summary>
internal sealed class QuietClipboard(IClipboardSurface inner) : IClipboardSurface
{
    public ClipboardContent? Read()
    {
        if (ClipboardQuiet.Active)
        {
            return null;
        }

        var content = inner.Read();
        return ClipboardQuiet.Active ? null : content;
    }

    public void Write(ClipboardContent content) => inner.Write(content);
}

/// <summary>
/// Raised while typing borrows the clipboard (<see cref="Edgepad.Input.PasteGuard"/>), so the poll in
/// <see cref="LinuxClipboard.Poll"/> neither sends the typed text nor the restore that follows it to the phone. A
/// counter, so two overlapping pastes nest. Process-wide, since the session's clipboard sync is built after the input
/// that types.
/// </summary>
internal static class ClipboardQuiet
{
    private static int depth;

    public static bool Active => Volatile.Read(ref depth) > 0;

    public static IDisposable Begin()
    {
        Interlocked.Increment(ref depth);
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                Interlocked.Decrement(ref depth);
            }
        }
    }
}
