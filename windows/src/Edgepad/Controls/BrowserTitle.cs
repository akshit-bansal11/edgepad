using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Edgepad.Controls;

/// <summary>
/// Netflix playing in Chrome reports itself to Windows as Chrome, so the phone would draw a C. The
/// service is named in the browser window's title ("Stranger Things | Netflix - Google Chrome"), so a
/// browser source is looked up there. Only the front tab titles its window: a service playing in a
/// background tab stays the browser's name.
/// </summary>
internal static partial class BrowserTitle
{
    private static readonly string[] Browsers = ["chrome", "msedge", "firefox", "brave", "opera", "vivaldi"];

    // Longer names first, so "YouTube Music" wins over "YouTube" and "Apple TV" over nothing at all.
    private static readonly string[] Services =
    [
        "YouTube Music", "Apple Music", "Apple TV", "Prime Video", "HBO Max", "Paramount+", "Disney+", "Netflix",
        "YouTube", "Spotify", "Hulu", "Crunchyroll", "Peacock", "SoundCloud", "Twitch", "JioHotstar", "Hotstar",
        "JioCinema", "SonyLIV",
    ];

    [ThreadStatic]
    private static HashSet<int>? processIds;

    [ThreadStatic]
    private static List<string>? found;

    public static bool IsBrowser(string executable) => Browsers.Contains(executable, StringComparer.OrdinalIgnoreCase);

    /// <summary>The first known service named in any of <paramref name="titles"/>, or null.</summary>
    public static string? Service(IEnumerable<string> titles)
    {
        foreach (var title in titles)
        {
            foreach (var service in Services)
            {
                if (title.Contains(service, StringComparison.OrdinalIgnoreCase))
                {
                    return service;
                }
            }
        }

        return null;
    }

    /// <summary>The titles of every visible top-level window of the processes called <paramref name="executable"/>.</summary>
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
    private static int OnWindow(nint window, nint lParam)
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

        var length = GetWindowTextLengthW(window);
        if (length > 0)
        {
            Span<char> text = stackalloc char[length + 1];
            var copied = GetWindowTextW(window, text, text.Length);
            titles.Add(new string(text[..copied]));
        }

        return 1;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(nint window);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextW(nint window, Span<char> text, int max);
}
