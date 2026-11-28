using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Edgepad.Media;

namespace Edgepad.Controls;

/// <summary>
/// Netflix playing in Chrome reports itself to Windows as Chrome, so the phone would draw a C. The
/// service is named in the browser window's title ("Stranger Things | Netflix - Google Chrome"), so a
/// browser source is looked up there. Only the front tab titles its window: a service playing in a
/// background tab stays the browser's name.
/// </summary>
internal static partial class BrowserTitle
{
    // The title length comes from another process, so the stack buffer is capped; a service name sits well inside it.
    private const int MaxTitleLength = 1024;

    private static readonly string[] Browsers = ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi"];

    [ThreadStatic]
    private static HashSet<int>? processIds;

    [ThreadStatic]
    private static List<string>? found;

    public static bool IsBrowser(string executable) => Browsers.Contains(executable, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The service the window that is playing names, or null. <paramref name="titles"/> are the browser's windows, top of
    /// the z-order first. The window whose title holds <paramref name="track"/> is the one playing; with none (or no track),
    /// the top window is. Only that one window is read: another window naming a service says nothing about what plays.
    /// </summary>
    public static string? Service(IReadOnlyList<string> titles, string track)
    {
        if (titles.Count == 0)
        {
            return null;
        }

        var playing = track.Length == 0
            ? titles[0]
            : titles.FirstOrDefault(title => title.Contains(track, StringComparison.OrdinalIgnoreCase)) ?? titles[0];
        return MediaServices.FromTitle(playing);
    }

    /// <summary>The visible top-level windows of the processes called <paramref name="executable"/>, top of the z-order first (the title only).</summary>
    public static unsafe List<string> WindowTitles(string executable)
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName(executable))
        {
            ids.Add(process.Id);
            process.Dispose();
        }

        var titles = new List<string>();
        if (ids.Count == 0)
        {
            return titles;
        }

        processIds = ids;
        found = titles;
        try
        {
            EnumWindows(&OnWindow, 0);
        }
        finally
        {
            processIds = null;
            found = null;
        }

        return titles;
    }

    [UnmanagedCallersOnly]
    private static unsafe int OnWindow(nint window, nint lParam)
    {
        if (processIds is not { } ids || found is not { } titles || !IsWindowVisible(window))
        {
            return 1;
        }

        _ = GetWindowThreadProcessId(window, out var pid);
        if (!ids.Contains((int)pid))
        {
            return 1;
        }

        var length = Math.Min(GetWindowTextLengthW(window), MaxTitleLength);
        if (length > 0)
        {
            // A raw UTF-16 buffer: LibraryImport marshals a pointer with nothing to generate.
            var text = stackalloc char[length + 1];
            var copied = GetWindowTextW(window, text, length + 1);
            titles.Add(new string(text, 0, copied));
        }

        return 1;
    }

    /// <summary>
    /// The service last read for one media app and track, so the second-by-second media tick does not list every
    /// process and window each time: the titles are read again only when the app or the track changes. Holds one
    /// entry. Used under MediaSessions' one-publish-at-a-time gate.
    /// </summary>
    internal sealed class Cache(Func<string, List<string>> titles, Func<string> foreground)
    {
        private string? key;
        private string? service;

        public Cache()
            : this(WindowTitles, ForegroundKey)
        {
        }

        /// <summary>
        /// The service named by the window of <paramref name="executable"/> that plays <paramref name="track"/> (the media
        /// title), or null. Read again when the app, the line or the window in front changes, so a tab switch cannot keep
        /// the last answer.
        /// </summary>
        public string? Service(string executable, string appUserModelId, string line, string track)
        {
            var now = appUserModelId + "\n" + line + "\n" + foreground();
            if (now != key)
            {
                key = now;
                service = BrowserTitle.Service(titles(executable), track);
            }

            return service;
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint lParam);

    /// <summary>The window in front and its title: one cheap call, and different whenever the front window or its tab changes.</summary>
    private static unsafe string ForegroundKey()
    {
        var window = GetForegroundWindow();
        var length = Math.Min(GetWindowTextLengthW(window), MaxTitleLength);
        if (length <= 0)
        {
            return window.ToString(CultureInfo.InvariantCulture);
        }

        var text = stackalloc char[length + 1];
        var copied = GetWindowTextW(window, text, length + 1);
        return window.ToString(CultureInfo.InvariantCulture) + "|" + new string(text, 0, copied);
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(nint window);

    [LibraryImport("user32.dll")]
    private static unsafe partial int GetWindowTextW(nint window, char* text, int max);
}
