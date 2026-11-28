using System.Text.RegularExpressions;

namespace Edgepad.Media;

/// <summary>One streaming service: what the laptop sends as the media app's name, and how it is recognised.</summary>
internal sealed record MediaService(string Name, string[] Titles, string[] Hosts, string[] Apps, bool NoLogo);

/// <summary>
/// Which service is playing, for the phone's logo. The rows are the same as protocol/media-services.txt, in the same
/// order (a test fails on any difference); the first row that matches wins.
/// </summary>
internal static class MediaServices
{
    internal static readonly MediaService[] Rows =
    [
        new("YouTube Music", ["YouTube Music"], ["music.youtube.com"], [], false),
        new("Apple Music", ["Apple Music"], ["music.apple.com"], ["applemusic", "itunes"], false),
        new("Apple TV+", ["Apple TV+"], [], [], false),
        new("Apple TV", ["Apple TV"], ["tv.apple.com"], ["appletv"], false),
        new("Prime Video", ["Prime Video"], ["primevideo.com"], ["primevideo", "amazonvideo"], false),
        new("HBO Max", ["HBO Max"], ["max.com", "hbomax.com"], [], false),
        new("Paramount+", ["Paramount+"], ["paramountplus.com"], [], false),
        new("Disney+", ["Disney+"], ["disneyplus.com"], ["disney"], false),
        new("Netflix", ["Netflix"], ["netflix.com"], ["netflix"], false),
        new("YouTube", ["YouTube"], ["youtube.com", "youtu.be"], [], false),
        new("Spotify", ["Spotify"], ["spotify.com"], ["spotify"], false),
        new("Hulu", ["Hulu"], ["hulu.com"], ["hulu"], false),
        new("Crunchyroll", ["Crunchyroll"], ["crunchyroll.com"], ["crunchyroll"], false),
        new("Peacock", ["Peacock"], ["peacocktv.com"], [], false),
        new("Instagram", ["Instagram"], ["instagram.com"], ["instagram"], false),
        new("Facebook", ["Facebook"], ["facebook.com"], ["facebook"], false),
        new("LinkedIn", ["LinkedIn"], ["linkedin.com"], [], false),
        new("X", ["Twitter"], ["x.com", "twitter.com"], [], false),
        new("Sky", ["Sky Go", "NOW TV"], ["sky.com"], [], false),
        new("SoundCloud", ["SoundCloud"], ["soundcloud.com"], [], true),
        new("Twitch", ["Twitch"], ["twitch.tv"], [], true),
        new("Plex", ["Plex"], ["plex.tv"], ["plex"], true),
        new("JioHotstar", ["JioHotstar", "Hotstar"], ["hotstar.com"], [], true),
        new("JioCinema", ["JioCinema"], ["jiocinema.com"], [], true),
        new("SonyLIV", ["SonyLIV"], ["sonyliv.com"], [], true),
    ];

    // The name must stand as its own word: no letter or digit right before or after, so "Plex" is never read out of
    // "complexity". The trailing lookahead is deliberately not \b: "Paramount+" and "Disney+" end in a non-word
    // character, which has no word boundary after it.
    private static readonly Regex[][] TitlePatterns =
    [
        .. Rows.Select(row => row.Titles
            .Select(words => new Regex(
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(words)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(200)))
            .ToArray()),
    ];

    /// <summary>The service a browser tab or window title names, or null.</summary>
    internal static string? FromTitle(string title)
    {
        for (var i = 0; i < Rows.Length; i++)
        {
            if (TitlePatterns[i].Any(pattern => pattern.IsMatch(title)))
            {
                return Rows[i].Name;
            }
        }

        return null;
    }

    /// <summary>The service a page address or bare host belongs to (a sub-domain counts), or null.</summary>
    internal static string? FromHost(string urlOrHost)
    {
        var host = HostOf(urlOrHost);
        if (host.Length == 0)
        {
            return null;
        }

        return Rows.FirstOrDefault(row => row.Hosts.Any(h =>
            host.Equals(h, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase)))?.Name;
    }

    /// <summary>The service a desktop app's app id or exe name holds, or null.</summary>
    internal static string? FromApp(string appId) =>
        appId.Length == 0
            ? null
            : Rows.FirstOrDefault(row => row.Apps.Any(token => appId.Contains(token, StringComparison.OrdinalIgnoreCase)))?.Name;

    private static string HostOf(string urlOrHost)
    {
        var text = urlOrHost.Trim();
        if (text.Contains("://", StringComparison.Ordinal))
        {
            return Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri.Host : "";
        }

        var end = text.IndexOfAny(['/', '?', '#']);
        return end >= 0 ? text[..end] : text;
    }
}
