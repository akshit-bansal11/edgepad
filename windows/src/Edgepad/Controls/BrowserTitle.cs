using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

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
    // A name does not have to be a coined word: Patterns below requires it to stand as its own word,
    // so "Plex" can go on this list without being read out of "complexity".
    private static readonly string[] Services =
    [
        "YouTube Music", "Apple Music", "Apple TV", "Prime Video", "HBO Max", "Paramount+", "Disney+", "Netflix",
        "YouTube", "Spotify", "Hulu", "Crunchyroll", "Peacock", "SoundCloud", "Twitch", "Plex", "JioHotstar", "Hotstar",
        "JioCinema", "SonyLIV",
    ];

    /// <summary>
    /// One pattern per name in <see cref="Services"/>, in the same order. A bare Contains would read
    /// "Plex" out of "Time complexity" and "Twitch" out of "Twitches", and <see cref="Service"/> returns
    /// on the first <em>title</em> that matches anything — so one unrelated background tab could name the
    /// wrong service while the tab actually playing said so plainly in its own title.
    /// <para>
    /// The trailing <c>(?!\w)</c> is deliberately not a second <c>\b</c>. "Paramount+" and "Disney+" end
    /// in a non-word character, which has no word boundary after it, so <c>\b…\b</c> matches neither:
    /// it would trade one wrong label for two missing ones.
    /// </para>
    /// </summary>
    private static readonly Regex[] Patterns =
        [.. Services.Select(service => new Regex($@"\b{Regex.Escape(service)}(?!\w)", RegexOptions.IgnoreCase))];

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
            for (var i = 0; i < Patterns.Length; i++)
            {
                if (Patterns[i].IsMatch(title))
                {
                    return Services[i];
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

        var length = GetWindowTextLengthW(window);
        if (length > 0)
        {
            // A raw UTF-16 buffer: LibraryImport marshals a pointer with nothing to generate.
            var text = stackalloc char[length + 1];
            var copied = GetWindowTextW(window, text, length + 1);
            titles.Add(new string(text, 0, copied));
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
    private static unsafe partial int GetWindowTextW(nint window, char* text, int max);
}
