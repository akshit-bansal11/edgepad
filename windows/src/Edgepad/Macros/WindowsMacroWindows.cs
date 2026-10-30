using System.Runtime.InteropServices;
using Edgepad.Injection;

namespace Edgepad.Macros;

/// <summary>
/// The windows of macro apps, found by matching each app window's process image against the macro's program. Every
/// call into another app here is an asynchronous post, never a message that waits for an answer, so a hung app
/// cannot stall the pool thread it is called from. Closing posts WM_CLOSE, which is what the window's own X sends:
/// the app may ask to save, and nothing is ever killed.
/// </summary>
internal sealed partial class WindowsMacroWindows : IMacroWindows
{
    private const int MaxPath = 1024;
    private const uint OwnerWindow = 4; // GW_OWNER
    private const int ExtendedStyle = -20; // GWL_EXSTYLE
    private const int ToolWindow = 0x80; // WS_EX_TOOLWINDOW
    private const uint Cloaked = 14; // DWMWA_CLOAKED
    private const uint RootOwner = 3; // GA_ROOTOWNER
    private const uint QueryLimitedInformation = 0x1000; // PROCESS_QUERY_LIMITED_INFORMATION
    private const int Minimized = 6; // SW_MINIMIZE
    private const int Restored = 9; // SW_RESTORE
    private const uint CloseMessage = 0x0010; // WM_CLOSE

    [ThreadStatic]
    private static List<AppWindow>? found;

    [ThreadStatic]
    private static Dictionary<uint, string?>? images;

    public bool Available => true;

    public OpenState[] Survey(IReadOnlyList<Macro> macros)
    {
        var windows = AppWindows();
        var front = ForegroundRoot();
        return [.. macros.Select(macro => State(macro, windows, front))];
    }

    public bool Minimize(Macro macro)
    {
        var front = ForegroundRoot();
        return WindowsOf(macro).Contains(front) && ShowWindowAsync(front, Minimized);
    }

    public bool Raise(Macro macro)
    {
        var mine = WindowsOf(macro);
        if (mine.Count == 0)
        {
            return false;
        }

        // EnumWindows walks the Z order, so the first one is the topmost.
        var window = mine[0];
        var restored = IsIconic(window) && ShowWindowAsync(window, Restored);
        if (TakeFront(window))
        {
            Log.Write($"Macro app {macro.Name} came to the front directly");
            return true;
        }

        // Windows refuses the front to a background process unless it was the last to get input, and an Alt tap
        // makes it that: the known way round the foreground lock.
        _ = NativeMethods.SendInput(
            2, [InputBuilder.Key(Keys.Menu, up: false), InputBuilder.Key(Keys.Menu, up: true)], Marshal.SizeOf<NativeInput>());
        var taken = TakeFront(window);
        Log.Write($"Macro app {macro.Name} {(taken ? "came to the front after an Alt tap" : "stayed behind, even after an Alt tap")}");
        return taken || restored;
    }

    public bool Close(Macro macro)
    {
        var posted = false;
        foreach (var window in WindowsOf(macro))
        {
            posted |= PostMessageW(window, CloseMessage, 0, 0);
        }

        return posted;
    }

    private static OpenState State(Macro macro, List<AppWindow> windows, nint front)
    {
        if (Program(macro) is not { } program)
        {
            return OpenState.Untracked;
        }

        var mine = Matching(windows, program);
        if (mine.Count == 0)
        {
            return OpenState.Closed;
        }

        return mine.Contains(front) ? OpenState.Front : OpenState.Open;
    }

    private static string? Program(Macro macro) => MacroTarget.Program(macro.Target, File.Exists, ShortcutTarget.Resolve);

    private static List<nint> WindowsOf(Macro macro) => Program(macro) is { } program ? Matching(AppWindows(), program) : [];

    private static List<nint> Matching(List<AppWindow> windows, string program) =>
        [.. windows.Where(window => MacroTarget.Same(window.Image, program)).Select(window => window.Handle)];

    private static bool TakeFront(nint window)
    {
        _ = SetForegroundWindow(window);
        return ForegroundRoot() == window;
    }

    /// <summary>The app window behind the foreground window: a save prompt is owned by it, and has no place in the list.</summary>
    private static nint ForegroundRoot() => GetAncestor(GetForegroundWindow(), RootOwner);

    /// <summary>The app windows on this desktop, topmost first. One pass, and one image read per process.</summary>
    private static unsafe List<AppWindow> AppWindows()
    {
        var windows = new List<AppWindow>();
        found = windows;
        images = new Dictionary<uint, string?>();
        try
        {
            EnumWindows(&OnWindow, 0);
        }
        finally
        {
            found = null;
            images = null;
        }

        return windows;
    }

    [UnmanagedCallersOnly]
    private static int OnWindow(nint window, nint lParam)
    {
        if (found is { } windows && images is { } known && IsAppWindow(window))
        {
            _ = GetWindowThreadProcessId(window, out var pid);
            if (!known.TryGetValue(pid, out var image))
            {
                image = ImageOf(pid);
                known[pid] = image;
            }

            windows.Add(new AppWindow(window, image));
        }

        return 1;
    }

    /// <summary>What the taskbar would show. Cloaked also hides other virtual desktops and suspended Store apps.</summary>
    private static bool IsAppWindow(nint window) =>
        IsWindowVisible(window)
        && GetWindow(window, OwnerWindow) == 0
        && (GetWindowLongW(window, ExtendedStyle) & ToolWindow) == 0
        && !(DwmGetWindowAttribute(window, Cloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0);

    /// <summary>The process's image path, or null, as for an elevated process, which then matches nothing.</summary>
    private static unsafe string? ImageOf(uint pid)
    {
        var process = OpenProcess(QueryLimitedInformation, false, pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var text = stackalloc char[MaxPath];
            var size = (uint)MaxPath;
            return QueryFullProcessImageNameW(process, 0, text, ref size) ? new string(text, 0, (int)size) : null;
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(nint window, int index);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint window, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindowAsync(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint window, uint attribute, out int value, uint size);

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(nint process, uint flags, char* name, ref uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    private readonly record struct AppWindow(nint Handle, string? Image);
}
