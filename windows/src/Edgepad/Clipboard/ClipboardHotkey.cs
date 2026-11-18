using System.Runtime.InteropServices;
using Edgepad.Injection;

// Not Edgepad.Clipboard, for the reason WindowsClipboard.cs gives.
namespace Edgepad.Blob;

/// <summary>
/// The global shortcut that opens the clipboard panel. Made on the UI thread: its message-only window receives
/// WM_HOTKEY there, so <c>pressed</c> runs on that thread. Another app may already own the key; that is logged once
/// and the tray row still opens the panel.
/// </summary>
internal sealed class ClipboardHotkey : IDisposable
{
    public const string Display = "Win+Alt+V";

    private const int Id = 1;

    private readonly Window window;

    public ClipboardHotkey(Action pressed)
    {
        window = new Window(pressed);
        window.CreateHandle(new CreateParams { Parent = NativeMethods.MessageOnlyParent });
        Registered = NativeMethods.RegisterHotKey(
            window.Handle,
            Id,
            NativeMethods.ModWin | NativeMethods.ModAlt | NativeMethods.ModNoRepeat,
            (uint)Keys.V);
        if (!Registered)
        {
            Log.Write($"{Display} could not be registered: error {Marshal.GetLastPInvokeError()}");
        }
    }

    /// <summary>False when another app owns the key.</summary>
    public bool Registered { get; private set; }

    public void Dispose()
    {
        if (Registered)
        {
            _ = NativeMethods.UnregisterHotKey(window.Handle, Id);
            Registered = false;
        }

        window.DestroyHandle();
    }

    private sealed class Window(Action pressed) : NativeWindow
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WmHotkey && m.WParam == Id)
            {
                pressed();
            }

            base.WndProc(ref m);
        }
    }
}

/// <summary>The keystrokes that paste into whatever window has the focus.</summary>
internal static class PasteKeys
{
    public static NativeInput[] CtrlV() => InputBuilder.Chord(Keys.ControlKey, Keys.V);
}
