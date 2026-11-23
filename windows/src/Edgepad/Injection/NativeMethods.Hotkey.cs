using System.Runtime.InteropServices;

namespace Edgepad.Injection;

/// <summary>The global hotkey and foreground-window calls the clipboard panel needs.</summary>
internal static partial class NativeMethods
{
    /// <summary>WM_HOTKEY, posted to the window that registered the key.</summary>
    public const int WmHotkey = 0x0312;

    public const uint ModAlt = 0x0001;
    public const uint ModWin = 0x0008;

    /// <summary>A held key fires the hotkey once, not on every auto-repeat.</summary>
    public const uint ModNoRepeat = 0x4000;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint window, int id);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint window);

    /// <summary>High bit set while the key is down.</summary>
    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int key);
}
