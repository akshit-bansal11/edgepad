using System.Globalization;
using Edgepad.Media;
using Edgepad.Protocol;
using Tmds.DBus.Protocol;

namespace Edgepad.Platform;

/// <summary>A snapshot of what the laptop is playing, as the phone's media corner shows it.</summary>
internal sealed record MediaState(
    string NowPlaying,
    string App,
    bool Playing,
    int PositionPercent,
    int PositionSeconds,
    int DurationSeconds,
    string Full = "",
    string Bus = "",
    string ArtUrl = "")
{
    public static MediaState Nothing { get; } = new("", "", false, 0, 0, 0);
}

/// <summary>
/// One MPRIS player as read off the bus: its bus name, <c>PlaybackStatus</c>, <c>Position</c> in microseconds, and
/// its <c>Metadata</c> as plain values (strings, a string array, numbers), so the mapping below is a pure function
/// of data a test can write down.
/// </summary>
internal sealed record PlayerSnapshot(
    string BusName,
    string PlaybackStatus,
    long PositionMicroseconds,
    IReadOnlyDictionary<string, object?> Metadata);

/// <summary>What the media corner shows, worked out from MPRIS data. No bus, no processes.</summary>
internal static class MprisMap
{
    public const string BusPrefix = "org.mpris.MediaPlayer2.";
    public const string NoTrack = "/org/mpris/MediaPlayer2/TrackList/NoTrack";

    // A TEXT payload holds 255 bytes; a title is cut well inside that, on a whole character.
    private const int MaxTextBytes = 200;

    /// <summary>
    /// The player the phone's corner follows: one that is playing, else one that is paused, else the first. Among
    /// equals the one followed last time stays, so two players playing at once do not take turns, and otherwise the
    /// first on the bus (the longest-running) wins. Null with no players.
    /// </summary>
    public static PlayerSnapshot? Choose(IReadOnlyList<PlayerSnapshot> players, string? previous)
    {
        PlayerSnapshot? best = null;
        var bestRank = int.MaxValue;
        foreach (var player in players)
        {
            var rank = Rank(player);
            if (rank < bestRank || (rank == bestRank && player.BusName == previous))
            {
                best = player;
                bestRank = rank;
            }
        }

        return best;
    }

    public static MediaState ToState(PlayerSnapshot player)
    {
        var title = Text(player.Metadata, "xesam:title");
        var artist = player.Metadata.TryGetValue("xesam:artist", out var raw) && raw is string[] artists
            ? string.Join(", ", artists.Where(a => !string.IsNullOrWhiteSpace(a)))
            : Text(player.Metadata, "xesam:artist");
        var full = string.IsNullOrWhiteSpace(artist) ? title : $"{title} - {artist}";
        var nowPlaying = TitleCleaner.NowPlaying(title, artist);

        var length = Length(player.Metadata);
        var position = Math.Clamp(player.PositionMicroseconds, 0, length > 0 ? length : long.MaxValue);
        var percent = length > 0 ? (int)Math.Clamp(position * 100 / length, 0, 100) : 0;
        return new MediaState(
            FrameCodec.Truncate(nowPlaying.Trim(), MaxTextBytes),
            ServiceName(player, title),
            player.PlaybackStatus == "Playing",
            percent,
            length > 0 ? (int)(position / 1_000_000) : 0,
            (int)(length / 1_000_000),
            FrameCodec.Truncate(full.Trim(), MaxTextBytes),
            player.BusName,
            Text(player.Metadata, "mpris:artUrl"));
    }

    private const long MaxArtworkBytes = 512 * 1024;

    /// <summary>
    /// What the phone is told the app is. A browser is named after the service its page address (<c>xesam:url</c>) or, with
    /// none, its title names, so the phone draws that service's logo; a browser with neither stays the browser's name, for
    /// which the phone has no logo and shows its plain symbol. Another player keeps its bus name.
    /// </summary>
    internal static string ServiceName(PlayerSnapshot player, string title)
    {
        var app = AppName(player.BusName);
        if (!IsBrowser(player.BusName))
        {
            return app;
        }

        var url = Text(player.Metadata, "xesam:url");
        return (url.Length > 0 ? MediaServices.FromHost(url) : null) ?? MediaServices.FromTitle(title) ?? app;
    }

    /// <summary>Players that play web pages, whose own icon says nothing about what is playing.</summary>
    public static bool IsBrowser(string busName) =>
        AppName(busName).ToUpperInvariant() is "FIREFOX" or "CHROMIUM" or "CHROME" or "BRAVE" or "VIVALDI" or "OPERA" or "MSEDGE" or "EDGE";

    /// <summary>
    /// The local picture <c>mpris:artUrl</c> names, or null. Only a <c>file:</c> png or jpg under 512 KB:
    /// the laptop never fetches a web image. <paramref name="sizeOf"/> gives a file's size, null when it is missing.
    /// </summary>
    public static string? ArtworkPath(string? artUrl, Func<string, long?> sizeOf)
    {
        if (string.IsNullOrEmpty(artUrl)
            || !Uri.TryCreate(artUrl, UriKind.Absolute, out var uri)
            || !uri.IsFile
            || uri.Host.Length > 0
            || Path.GetExtension(uri.LocalPath).ToUpperInvariant() is not (".PNG" or ".JPG" or ".JPEG"))
        {
            return null;
        }

        return sizeOf(uri.LocalPath) is > 0 and <= MaxArtworkBytes ? uri.LocalPath : null;
    }

    /// <summary>
    /// "spotify" from <c>org.mpris.MediaPlayer2.spotify</c> and "Firefox" from
    /// <c>org.mpris.MediaPlayer2.firefox.instance_1_234</c>: the name the player took on the bus, without a
    /// per-instance suffix, capitalised. The player's own display name (its <c>Identity</c>) would take another
    /// call per player on every poll for a label.
    /// </summary>
    public static string AppName(string busName)
    {
        var name = busName.StartsWith(BusPrefix, StringComparison.Ordinal) ? busName[BusPrefix.Length..] : busName;
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
        {
            name = name[..dot];
        }

        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>
    /// Whether a foreground token (the lower-cased WM_CLASS class, "firefox" or "chromium-browser") and a playing app's name
    /// ("Firefox", "Chromium") are the same app: equal ignoring case, or the token is the name and a "-suffix".
    /// </summary>
    // HW: verify on device (WM_CLASS against the MPRIS bus name of Firefox, Chromium, Spotify and VLC).
    public static bool SameLinux(string token, string appName) =>
        appName.Length > 0
        && (string.Equals(token, appName, StringComparison.OrdinalIgnoreCase)
            || token.StartsWith(appName + "-", StringComparison.OrdinalIgnoreCase));

    /// <summary>The track's <c>mpris:trackid</c> for SetPosition, or null when there is none to seek in.</summary>
    public static string? TrackId(PlayerSnapshot player) =>
        player.Metadata.TryGetValue("mpris:trackid", out var id) && id is string path && path.Length > 0 && path != NoTrack ? path : null;

    /// <summary>The track length in microseconds (<c>mpris:length</c>), 0 when the player gives none.</summary>
    public static long Length(IReadOnlyDictionary<string, object?> metadata) =>
        metadata.TryGetValue("mpris:length", out var raw) && raw is IConvertible and not string
            ? Math.Max(0, Convert.ToInt64(raw, CultureInfo.InvariantCulture))
            : 0;

    /// <summary>The microsecond position for <paramref name="percent"/> of the track.</summary>
    public static long PositionFor(int percent, long length) => length * Math.Clamp(percent, 0, 100) / 100;

    private static int Rank(PlayerSnapshot player) => player.PlaybackStatus switch
    {
        "Playing" => 0,
        "Paused" => 1,
        _ => 2,
    };

    private static string Text(IReadOnlyDictionary<string, object?> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && value is string text ? text : "";
}

/// <summary>
/// Media over MPRIS, the D-Bus interface every Linux player of consequence (Spotify, Firefox, Chromium, VLC, mpv
/// with a plugin) exports on the session bus as <c>org.mpris.MediaPlayer2.NAME</c>. <see cref="Poll"/> reads every
/// player and follows the best one (<see cref="MprisMap.Choose"/>); play/pause, next, previous and seeking go to that
/// player. All calls block on the bus, so they run on the poll and worker threads, never the session's reader.
/// </summary>
internal sealed class Mpris : IDisposable
{
    private const string PlayerPath = "/org/mpris/MediaPlayer2";
    private const string PlayerInterface = "org.mpris.MediaPlayer2.Player";

    // More than this many players is not a real desktop; the cap bounds the calls one poll can make.
    private const int MaxPlayers = 8;

    private readonly Bus bus = Bus.OnSession();
    private readonly Lock gate = new();
    private readonly Dictionary<string, string> entries = [];
    private PlayerSnapshot? followed;

    /// <summary>
    /// Reads the players and returns what the best one is doing; <see cref="MediaState.Nothing"/> with none, or when
    /// the session bus cannot be reached.
    /// </summary>
    public MediaState Poll()
    {
        var choice = Choose();
        return choice is null ? MediaState.Nothing : MprisMap.ToState(choice);
    }

    /// <summary>
    /// Asks the followed player to PlayPause, Next or Previous. False, so the media key goes instead, only when there
    /// is no player to ask: none found, or the one asked is gone or has no such method. A slow player that missed the
    /// deadline may still act, and a key on top of it would undo that.
    /// </summary>
    public bool Command(string method)
    {
        var name = (Followed() ?? Choose())?.BusName;
        if (name is null)
        {
            return false;
        }

        var answered = bus.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(destination: name, path: PlayerPath, @interface: PlayerInterface, member: method);
                return writer.CreateMessage();
            },
            out var refusal);
        return Handled(answered, refusal);
    }

    /// <summary>
    /// Whether a command's outcome leaves the media key out: anything but an error saying the player or the method
    /// is not there. <paramref name="refusal"/> is null for an unanswered call.
    /// </summary>
    internal static bool Handled(bool answered, string? refusal) =>
        answered || refusal is not ("org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner"
            or "org.freedesktop.DBus.Error.UnknownMethod" or "org.freedesktop.DBus.Error.UnknownObject"
            or "org.freedesktop.DBus.Error.UnknownInterface");

    /// <summary>Jumps to <paramref name="percent"/> of the track. False when nothing is playing or the player cannot seek.</summary>
    public bool Seek(int percent)
    {
        var player = Followed() ?? Choose();
        if (player is null || MprisMap.TrackId(player) is not { } track)
        {
            return false;
        }

        var length = MprisMap.Length(player.Metadata);
        if (length <= 0)
        {
            return false;
        }

        var micros = MprisMap.PositionFor(percent, length);
        return bus.Call(c =>
        {
            using var writer = c.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: player.BusName,
                path: PlayerPath,
                @interface: PlayerInterface,
                member: "SetPosition",
                signature: "ox");
            writer.WriteObjectPath(track);
            writer.WriteInt64(micros);
            return writer.CreateMessage();
        });
    }

    /// <summary>
    /// The player's <c>DesktopEntry</c> (its .desktop id, without the suffix), or "" when it names none. One
    /// <c>GetAll</c> per bus name, kept; a failed call is not kept, so it is asked again.
    /// </summary>
    public string DesktopEntry(string busName)
    {
        lock (gate)
        {
            if (entries.TryGetValue(busName, out var known))
            {
                return known;
            }
        }

        var read = bus.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(
                    destination: busName,
                    path: PlayerPath,
                    @interface: "org.freedesktop.DBus.Properties",
                    member: "GetAll",
                    signature: "s");
                writer.WriteString("org.mpris.MediaPlayer2");
                return writer.CreateMessage();
            },
            (Message reply, object? _) => reply.GetBodyReader().ReadDictionaryOfStringToVariantValue(),
            out var properties);
        if (!read)
        {
            return "";
        }

        var id = properties is not null && properties.TryGetValue("DesktopEntry", out var v) && Plain(v) is string text ? text : "";
        lock (gate)
        {
            // More names than this is not a real desktop (instances come and go): start over rather than grow.
            if (entries.Count >= MaxPlayers * 2)
            {
                entries.Clear();
            }

            entries[busName] = id;
        }

        return id;
    }

    public void Dispose() => bus.Dispose();

    private PlayerSnapshot? Followed()
    {
        lock (gate)
        {
            return followed;
        }
    }

    private PlayerSnapshot? Choose()
    {
        var names = ListPlayers();
        var players = new List<PlayerSnapshot>(names.Count);
        foreach (var name in names)
        {
            if (Snapshot(name) is { } snapshot)
            {
                players.Add(snapshot);
            }
        }

        lock (gate)
        {
            followed = MprisMap.Choose(players, followed?.BusName);
            return followed;
        }
    }

    private List<string> ListPlayers()
    {
        var listed = bus.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(
                    destination: "org.freedesktop.DBus",
                    path: "/org/freedesktop/DBus",
                    @interface: "org.freedesktop.DBus",
                    member: "ListNames");
                return writer.CreateMessage();
            },
            (Message reply, object? _) => reply.GetBodyReader().ReadArrayOfString(),
            out var names);
        return listed && names is not null
            ? names.Where(n => n.StartsWith(MprisMap.BusPrefix, StringComparison.Ordinal)).Take(MaxPlayers).ToList()
            : [];
    }

    private PlayerSnapshot? Snapshot(string busName)
    {
        var read = bus.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(
                    destination: busName,
                    path: PlayerPath,
                    @interface: "org.freedesktop.DBus.Properties",
                    member: "GetAll",
                    signature: "s");
                writer.WriteString(PlayerInterface);
                return writer.CreateMessage();
            },
            (Message reply, object? _) => reply.GetBodyReader().ReadDictionaryOfStringToVariantValue(),
            out var properties);
        if (!read || properties is null)
        {
            return null;
        }

        var status = properties.TryGetValue("PlaybackStatus", out var s) && Plain(s) is string text ? text : "Stopped";
        var position = properties.TryGetValue("Position", out var p) && Plain(p) is IConvertible raw and not string
            ? Convert.ToInt64(raw, CultureInfo.InvariantCulture)
            : 0;
        var metadata = new Dictionary<string, object?>();
        if (properties.TryGetValue("Metadata", out var m) && m.Type == VariantValueType.Dictionary)
        {
            foreach (var (key, value) in m.GetDictionary<string, VariantValue>())
            {
                metadata[key] = Plain(value);
            }
        }

        return new PlayerSnapshot(busName, status, position, metadata);
    }

    /// <summary>
    /// A variant's value as a plain object: a string (an object path too), a number, or a string array; null for
    /// anything else, which the mapping treats as absent. Players are free to send odd types (a length as a
    /// signed or an unsigned 64-bit integer, an artist as one string), so nothing here may throw on one.
    /// </summary>
    private static object? Plain(VariantValue value)
    {
        try
        {
            return value.Type switch
            {
                VariantValueType.String => value.GetString(),
                VariantValueType.ObjectPath => value.GetObjectPathAsString(),
                VariantValueType.Int64 => value.GetInt64(),
                VariantValueType.UInt64 => (long)Math.Min(value.GetUInt64(), long.MaxValue),
                VariantValueType.Int32 => (long)value.GetInt32(),
                VariantValueType.UInt32 => (long)value.GetUInt32(),
                VariantValueType.Double => (long)value.GetDouble(),
                VariantValueType.Array => value.GetArray<string>(),
                _ => null,
            };
        }
        catch (Exception e) when (e is InvalidCastException or InvalidOperationException or ArgumentException or DBusExceptionBase)
        {
            return null;
        }
    }
}
