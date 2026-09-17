using Windows.Media.Control;

namespace Edgepad.Controls;

/// <summary>A snapshot of what the laptop is playing, as the phone's media corner shows it.</summary>
internal sealed record MediaState(
    string NowPlaying,
    string App,
    bool Playing,
    int PositionPercent,
    int PositionSeconds,
    int DurationSeconds);

/// <summary>
/// What Windows itself knows about media: the system media transport controls that every player with a
/// taskbar-thumbnail play button reports to. Gives the title, the app, play state and position, and can
/// seek in players that allow it. Nothing here touches WinRT until <see cref="StartAsync"/>.
/// </summary>
internal sealed class MediaSessions : IDisposable
{
    private const int TickMs = 1000;

    private readonly Lock gate = new();
    private readonly List<Action<MediaState>> watchers = [];
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private MediaState? published;
    private System.Threading.Timer? ticker;

    public static MediaState Nothing { get; } = new("", "", false, 0, 0, 0);

    public async Task StartAsync()
    {
        manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        manager.CurrentSessionChanged += (_, _) => Attach(manager.GetCurrentSession());
        Attach(manager.GetCurrentSession());
        // Players report their position only now and then; while playing, the phone's scrub dial is
        // kept moving by extrapolating from the last report once a second. The tick is also the only
        // clock a title-only player has, since it raises no event to attach to.
        ticker = new System.Threading.Timer(_ => _ = PublishAsync(onTick: true), null, TickMs, TickMs);
    }

    /// <summary>Reports every change until the handle is disposed, starting with the current state.</summary>
    public IDisposable Watch(Action<MediaState> onChange)
    {
        lock (gate)
        {
            watchers.Add(onChange);
        }

        _ = SnapshotAsync().ContinueWith(t => onChange(t.Result), TaskContinuationOptions.OnlyOnRanToCompletion);
        return new Subscription(() =>
        {
            lock (gate)
            {
                watchers.Remove(onChange);
            }
        });
    }

    /// <summary>Jumps to a point in the current track. False when nothing is playing or the player refuses seeking.</summary>
    public bool Seek(int percent)
    {
        var current = session;
        if (current is null || !current.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled)
        {
            return false;
        }

        var timeline = current.GetTimelineProperties();
        var span = timeline.EndTime - timeline.StartTime;
        if (span <= TimeSpan.Zero)
        {
            return false;
        }

        var target = timeline.StartTime + span * (percent / 100.0);
        _ = current.TryChangePlaybackPositionAsync(target.Ticks);
        return true;
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? next)
    {
        var previous = Interlocked.Exchange(ref session, next);
        if (previous is not null)
        {
            previous.MediaPropertiesChanged -= OnChanged;
            previous.PlaybackInfoChanged -= OnChanged;
            previous.TimelinePropertiesChanged -= OnChanged;
        }

        if (next is not null)
        {
            next.MediaPropertiesChanged += OnChanged;
            next.PlaybackInfoChanged += OnChanged;
            next.TimelinePropertiesChanged += OnChanged;
        }

        _ = PublishAsync(onTick: false);
    }

    private void OnChanged(GlobalSystemMediaTransportControlsSession sender, object args) => _ = PublishAsync(onTick: false);

    private async Task PublishAsync(bool onTick)
    {
        try
        {
            var state = await SnapshotAsync();
            // A tick has something to say while a position is moving, or when the state changed with no
            // event to announce it — which is every change a title-only player makes. Anything else would
            // be a Bluetooth frame a second saying what the phone already shows.
            if (onTick && !state.Playing && state == published)
            {
                return;
            }

            published = state;

            Action<MediaState>[] targets;
            lock (gate)
            {
                targets = [.. watchers];
            }

            foreach (var target in targets)
            {
                target(state);
            }
        }
        catch (Exception e)
        {
            // Top-level boundary of a fire-and-forget: a player vanishing mid-query must not kill the app.
            Log.Write($"Media state failed: {e.Message}");
        }
    }

    private async Task<MediaState> SnapshotAsync()
    {
        var current = session;
        if (current is null)
        {
            return TitleOnly();
        }

        var properties = await current.TryGetMediaPropertiesAsync();
        var playback = current.GetPlaybackInfo();
        var timeline = current.GetTimelineProperties();
        if (properties is null || playback is null || timeline is null)
        {
            return Nothing;
        }

        var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        var position = timeline.Position;
        if (playing)
        {
            position += DateTimeOffset.Now - timeline.LastUpdatedTime;
        }

        var span = timeline.EndTime - timeline.StartTime;
        var percent = span > TimeSpan.Zero ? (int)Math.Clamp((position - timeline.StartTime) / span * 100, 0, 100) : 0;

        var title = properties.Title ?? "";
        var artist = properties.Artist ?? "";
        var nowPlaying = string.IsNullOrWhiteSpace(artist) ? title : title + " - " + artist;
        var seconds = span > TimeSpan.Zero ? (int)Math.Clamp((position - timeline.StartTime).TotalSeconds, 0, span.TotalSeconds) : 0;
        var aumid = current.SourceAppUserModelId ?? "";
        var executable = Executable(aumid);
        // A browser is named after the service in its window title, when one can be seen there.
        var app = PlayerTitle.IsBrowser(executable)
            ? PlayerTitle.Service(PlayerTitle.WindowTitles(executable)) ?? AppName(aumid)
            : AppName(aumid);
        return new MediaState(nowPlaying.Trim(), app, playing, percent, seconds, (int)span.TotalSeconds);
    }

    /// <summary>
    /// Reached only when Windows reports no session at all, so a player that reports properly is never
    /// overridden by a window title. VLC 3.x is that case: it publishes nothing to the transport controls,
    /// and its title is the whole of what can be known. Everything a timeline needs is left at the values
    /// the phone already reads as "no timeline" — a zero length makes it hide the scrub rather than draw a
    /// 0:00 of 0:00 that would be a lie — and the play state is reported as unknown rather than guessed.
    /// </summary>
    private static MediaState TitleOnly() =>
        PlayerTitle.Playing() is { } playing ? new MediaState(playing.Media, playing.App, false, 0, 0, 0) : Nothing;

    /// <summary>"Spotify.exe" or "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" to something a label can show.</summary>
    internal static string AppName(string appUserModelId)
    {
        var name = Executable(appUserModelId);
        return name switch
        {
            "chrome" => "Chrome",
            "msedge" => "Edge",
            "firefox" => "Firefox",
            "vlc" => "VLC",
            "Microsoft.ZuneMusic" => "Media Player",
            "Microsoft.ZuneVideo" => "Films & TV",
            _ => name.Length == 0 ? "" : char.ToUpperInvariant(name[0]) + name[1..],
        };
    }

    /// <summary>The part of an app user model id that names the program: "chrome" from "chrome.exe", the id after the "!" for a packaged app.</summary>
    internal static string Executable(string appUserModelId)
    {
        var name = appUserModelId;
        var bang = name.LastIndexOf('!');
        if (bang >= 0)
        {
            name = name[(bang + 1)..];
        }

        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    public void Dispose()
    {
        ticker?.Dispose();
        Attach(null);
    }
}
