using System.Runtime.InteropServices;

namespace Edgepad.Injection;

/// <summary>
/// Typing into a Qt window without SendInput (#65). A Chinese IME (TSF) picks up injected VK_PACKET events,
/// swallows the key-up after a full-width punctuation mark and leaves the stream out of step; Qt consults
/// the IME state when it commits text, so in Weixin or Telegram <c>测试，你好</c> arrived as <c>测试，，好</c>.
/// Posting WM_CHAR to the focused window goes around both the IME and the input queue, and every native
/// control and Qt alike accept it.
///
/// Only Qt windows get it. Everything else keeps SendInput, because Windows Terminal ignores WM_CHAR, and
/// SendInput is what every other app has always been typed into here. Diagnosis and the measurements behind
/// the choice: https://github.com/akshit-bansal11/edgepad/issues/65 and BlueSend's docs/ime-typing-bug.md.
/// </summary>
internal static partial class QtTyping
{
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const int MaxClassName = 256;

    /// <summary>
    /// The window to post to when the keyboard focus is in a Qt window, else 0. A Qt window has no native
    /// child controls, so the focus window is the top-level one itself.
    /// </summary>
    public static unsafe nint Target()
    {
        var info = new GuiThreadInfo { Size = (uint)sizeof(GuiThreadInfo) };
        var window = GetGUIThreadInfo(0, ref info) && info.Focus != 0 ? info.Focus : GetForegroundWindow();
        if (window == 0)
        {
            return 0;
        }

        var name = stackalloc char[MaxClassName];
        var length = GetClassNameW(window, name, MaxClassName);
        return IsQtClass(new string(name, 0, Math.Max(length, 0))) ? window : 0;
    }

    /// <summary>
    /// Qt 5 and 6 name their window classes "Qt" + version digits + "QWindow…": Qt5QWindowIcon, or
    /// Qt51514QWindowIcon for a namespaced build (Telegram's), Qt662QWindowToolSaveBits and so on.
    /// </summary>
    public static bool IsQtClass(string name)
    {
        if (!name.StartsWith("Qt", StringComparison.Ordinal))
        {
            return false;
        }

        var i = 2;
        while (i < name.Length && char.IsAsciiDigit(name[i]))
        {
            i++;
        }

        return i > 2 && name.AsSpan(i).StartsWith("QWindow", StringComparison.Ordinal);
    }

    /// <summary>One UTF-16 unit as WM_CHAR. A character outside the BMP arrives as its two surrogates, in order.</summary>
    public static bool Char(nint window, char c) => PostMessageW(window, WmChar, (nint)c, 1);

    /// <summary>
    /// Backspace or Return as the key a keyboard would send: down, its character, up, all posted, so they
    /// stay in order with the WM_CHARs around them. SendInput here would jump the queue the other way:
    /// a window reads posted messages before input, so text typed after a newline would land before it.
    /// </summary>
    public static bool Key(nint window, ushort virtualKey, char c)
    {
        var scan = (nint)(NativeMethods.MapVirtualKeyW(virtualKey, 0) << 16);
        return PostMessageW(window, WmKeyDown, virtualKey, 1 | scan)
            & PostMessageW(window, WmChar, (nint)c, 1 | scan)
            & PostMessageW(window, WmKeyUp, virtualKey, unchecked((nint)0xC0000001) | scan);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public int CaretLeft;
        public int CaretTop;
        public int CaretRight;
        public int CaretBottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static unsafe partial int GetClassNameW(nint window, char* name, int capacity);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
