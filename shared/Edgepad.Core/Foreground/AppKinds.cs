namespace Edgepad.Foreground;

/// <summary>
/// Which kind of app is in front, for <see cref="Edgepad.Protocol.TextKind.ForegroundKind"/>. The table below is the same rows
/// as protocol/app-kinds.txt (a test fails on any difference): the key is what each OS's foreground reporter emits for the app.
/// </summary>
internal static class AppKinds
{
    private static readonly Dictionary<string, string> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["windows/spotify.exe"] = "media",
        ["windows/itunes.exe"] = "media",
        ["windows/applemusic.exe"] = "media",
        ["windows/vlc.exe"] = "media",
        ["windows/foobar2000.exe"] = "media",
        ["windows/musicbee.exe"] = "media",
        ["windows/wmplayer.exe"] = "media",
        ["windows/mpv.exe"] = "media",
        ["linux/spotify"] = "media",
        ["linux/vlc"] = "media",
        ["linux/mpv"] = "media",
        ["macos/com.spotify.client"] = "media",
        ["macos/com.apple.Music"] = "media",
        ["macos/com.apple.iTunes"] = "media",
        ["macos/org.videolan.vlc"] = "media",
        ["windows/blender.exe"] = "timeline",
        ["windows/afterfx.exe"] = "timeline",
        ["windows/resolve.exe"] = "timeline",
        ["windows/audacity.exe"] = "timeline",
        ["windows/fl64.exe"] = "timeline",
        ["windows/shotcut.exe"] = "timeline",
        ["windows/kdenlive.exe"] = "timeline",
        ["linux/blender"] = "timeline",
        ["linux/kdenlive"] = "timeline",
        ["linux/shotcut"] = "timeline",
        ["linux/audacity"] = "timeline",
        ["linux/resolve"] = "timeline",
        ["macos/org.blenderfoundation.blender"] = "timeline",
        ["macos/com.blackmagic-design.DaVinciResolve"] = "timeline",
        ["macos/org.audacityteam.audacity"] = "timeline",
        ["macos/com.apple.FinalCut"] = "timeline",
        ["macos/com.apple.logic10"] = "timeline",
        ["macos/com.ableton.live"] = "timeline",
        ["windows/chrome.exe"] = "browser",
        ["windows/msedge.exe"] = "browser",
        ["windows/firefox.exe"] = "browser",
        ["windows/brave.exe"] = "browser",
        ["windows/opera.exe"] = "browser",
        ["windows/vivaldi.exe"] = "browser",
        ["windows/arc.exe"] = "browser",
        ["linux/google-chrome"] = "browser",
        ["linux/chromium"] = "browser",
        ["linux/firefox"] = "browser",
        ["linux/brave-browser"] = "browser",
        ["linux/opera"] = "browser",
        ["linux/vivaldi-stable"] = "browser",
        ["linux/microsoft-edge"] = "browser",
        ["macos/com.google.Chrome"] = "browser",
        ["macos/com.microsoft.edgemac"] = "browser",
        ["macos/org.mozilla.firefox"] = "browser",
        ["macos/com.brave.Browser"] = "browser",
        ["macos/com.operasoftware.Opera"] = "browser",
        ["macos/com.vivaldi.Vivaldi"] = "browser",
        ["macos/com.apple.Safari"] = "browser",
        ["macos/company.thebrowser.Browser"] = "browser",
    };

    /// <summary>How many rows the table has, all three OS together.</summary>
    internal static int Count => Table.Count;

    /// <summary>The table's kind for this app, "media", "timeline" or "browser", or null when it is not listed.</summary>
    internal static string? Of(string os, string token) => Table.GetValueOrDefault($"{os}/{token}");

    /// <summary>
    /// Table kind "media" or "timeline" wins; else an app with a playing media session is "media"; else a table "browser" is
    /// "browser"; else empty.
    /// </summary>
    internal static string Resolve(string? tableKind, bool playing) =>
        tableKind is "media" or "timeline" ? tableKind
        : playing ? "media"
        : tableKind == "browser" ? "browser"
        : "";
}
