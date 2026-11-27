using System.Globalization;
using Edgepad.Blob;
using Edgepad.Macros;
using Edgepad.Net;
using Edgepad.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// Tells the phone what the laptop is doing, for the life of one session: the volume, microphone and brightness
/// dials, what is playing and the macro names. Linux has no change events
/// for these that every setup shares, so it polls twice a second like the Mac app and sends only what changed.
/// The volume, microphone and brightness reads start a process each, so they run twice a second only while the
/// phone sent input in the last 10 s, and every 3 s once it has been idle longer: a change the laptop makes itself
/// (its own volume keys) reaches an idle phone's dials within 3 s. Media is one cheap D-Bus call and stays at half a
/// second. The polling runs on a thread of its own. A send that fails means the socket is gone, which the session's
/// reader notices, so this just stops.
/// </summary>
internal sealed class StateReporter : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan IdleEvery = TimeSpan.FromSeconds(3);
    private const long ActiveWindowMs = 10_000;
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(2);

    private const byte MutedFlag = 1;
    private const byte PlayingFlag = 1;

    private readonly Audio speakers;
    private readonly Audio microphone;
    private readonly Backlight backlight;
    private readonly Mpris mpris;
    private readonly MacroStore macros;
    private readonly LinuxDisplayModes displayModes;
    private readonly Action<Frame> send;
    private readonly Func<long> lastInputMs;
    private readonly CancellationTokenSource stopping = new();
    private readonly Thread thread;

    // When the volume, microphone and brightness were last read; null before the first read.
    private long? lastLevelsRead;

    private Level? lastVolume;
    private Level? lastMic;
    private int? lastBrightness;
    private MediaState? lastMedia;
    private string? lastTimeline;
    private string? lastPlaying;
    private bool playingSent;
    private string lastFull = "";
    private string? lastRates;
    private int lastRate = -1;
    private string? lastIconKey;
    private readonly BlobSender blobs;

    private static volatile string? playingApp;

    /// <summary>
    /// Raised on the polling thread with the name of the app that plays (null when none does), only when it changes. Static
    /// because the media is the laptop's, not one session's: a listener is whichever session is watching.
    /// </summary>
    internal static event Action<string?>? MediaPlaying;

    /// <summary>The app that plays now, null when none does; what <see cref="MediaPlaying"/> last said.</summary>
    internal static string? PlayingApp
    {
        get => playingApp;
        private set => playingApp = value;
    }

    /// <param name="lastInputMs">When the phone last sent input, in <see cref="Environment.TickCount64"/> milliseconds.</param>
    public StateReporter(Audio speakers, Audio microphone, Backlight backlight, Mpris mpris, MacroStore macros, LinuxDisplayModes displayModes, Action<Frame> send, Func<long> lastInputMs)
    {
        this.displayModes = displayModes;
        this.lastInputMs = lastInputMs;
        this.speakers = speakers;
        this.microphone = microphone;
        this.backlight = backlight;
        this.mpris = mpris;
        this.macros = macros;
        this.send = send;

        // The smallest blobs there are, and Bluetooth's pace is the slower of the two, so it does for either link.
        blobs = new BlobSender(send, LinkKind.Bluetooth, lastInputMs, stopping.Token);
        thread = new Thread(Run) { IsBackground = true, Name = "edgepad-state" };
        thread.Start();
    }

    /// <summary>The text of a timeline: "seconds in/seconds long", empty when the player gives no length.</summary>
    public static string Timeline(MediaState state) => state.DurationSeconds > 0
        ? string.Create(CultureInfo.InvariantCulture, $"{state.PositionSeconds}/{state.DurationSeconds}")
        : "";

    /// <summary>How often the levels are read, given how long the phone has sent no input.</summary>
    public static TimeSpan IntervalFor(long idleMs) => idleMs < ActiveWindowMs ? Every : IdleEvery;

    public void Dispose()
    {
        stopping.Cancel();
        if (thread.Join(JoinTimeout))
        {
            stopping.Dispose();
        }
    }

    private void Run()
    {
        try
        {
            // The features (TEXT 9) are the annotator's to send, since annotate depends on the display. The
            // refresh-rate list is read once here (one xrandr run or D-Bus call), empty where the display cannot be
            // switched, which leaves the phone's dial with no travel.
            displayModes.Refresh();
            ReportRates();
            send(new Text((byte)TextKind.MacroCap, MacroStore.MaxMacros.ToString(CultureInfo.InvariantCulture)));

            // Reports the names now and again whenever the file is reloaded, and is released with this thread.
            using var watch = macros.Watch(names => Guarded(() => send(new Text((byte)TextKind.Macros, names))));

            while (!stopping.IsCancellationRequested)
            {
                Poll();
                stopping.Token.WaitHandle.WaitOne(Every);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The socket is gone; the session's reader ends the session.
        }
        catch (Exception e)
        {
            // A bug here costs the phone its live dials, not the session.
            Log.Write($"The state reporter stopped: {e}");
        }
    }

    private void Poll()
    {
        var now = Environment.TickCount64;
        if (lastLevelsRead is not { } read || now - read >= IntervalFor(now - lastInputMs()).TotalMilliseconds)
        {
            lastLevelsRead = now;
            PollLevels();
        }

        PollMedia();
        ReportRates();
    }

    /// <summary>
    /// TEXT 4 and then the index in force, each only when it changed. Reads fields the display modes already hold: the
    /// display is read on connect and after a switch, never from here.
    /// </summary>
    private void ReportRates()
    {
        var rates = string.Join('/', displayModes.Rates);
        if (rates != lastRates)
        {
            lastRates = rates;
            lastRate = -1;
            send(new Text((byte)TextKind.RefreshRates, rates));
        }

        var current = displayModes.Current;
        if (current != lastRate && current >= 0)
        {
            lastRate = current;
            send(new StateReport((byte)ControlId.RefreshRate, (byte)current, 0));
        }
    }

    private void PollLevels()
    {
        if (speakers.Read() is { } volume && volume != lastVolume)
        {
            lastVolume = volume;
            send(new StateReport((byte)ControlId.Volume, (byte)volume.Percent, volume.Muted ? MutedFlag : (byte)0));
        }

        if (microphone.Read() is { } mic && mic != lastMic)
        {
            lastMic = mic;
            send(new StateReport((byte)ControlId.MicLevel, (byte)mic.Percent, mic.Muted ? MutedFlag : (byte)0));
        }

        if (backlight.Read() is { } level && level != lastBrightness)
        {
            lastBrightness = level;
            send(new StateReport((byte)ControlId.Brightness, (byte)level, 0));
        }
    }

    private void PollMedia()
    {
        var media = mpris.Poll();

        // The app that plays, for the kind of app in front; raised only when it changes, and the latest kept for a watch that starts later.
        var playing = media.Playing && media.Bus.Length > 0 ? MprisMap.AppName(media.Bus) : null;
        if (!playingSent || playing != lastPlaying)
        {
            playingSent = true;
            lastPlaying = playing;
            PlayingApp = playing;
            MediaPlaying?.Invoke(playing);
        }
        // The uncleaned line goes first, only while it differs from TEXT 0; empty puts the phone back on TEXT 0.
        var full = FullFor(media);
        if (full != lastFull)
        {
            lastFull = full;
            send(new Text((byte)TextKind.NowPlayingFull, full));
        }

        if (media.NowPlaying != lastMedia?.NowPlaying)
        {
            send(new Text((byte)TextKind.NowPlaying, media.NowPlaying));
        }

        if (media.App != lastMedia?.App)
        {
            send(new Text((byte)TextKind.App, media.App));
        }

        var timeline = Timeline(media);
        if (timeline != lastTimeline)
        {
            lastTimeline = timeline;
            send(new Text((byte)TextKind.Timeline, timeline));
        }

        if (media.PositionPercent != lastMedia?.PositionPercent || media.Playing != lastMedia?.Playing)
        {
            send(new StateReport((byte)ControlId.MediaPosition, (byte)media.PositionPercent, media.Playing ? PlayingFlag : (byte)0));
        }

        SendIcon(media);
        lastMedia = media;
    }

    /// <summary>The TEXT 26 payload: the uncleaned line when cleaning changed it, else empty.</summary>
    internal static string FullFor(MediaState media) => media.Full == media.NowPlaying ? "" : media.Full;

    /// <summary>
    /// BLOB 4, once per followed player, and for a browser once per artwork. Nothing to do with no player. The
    /// artwork is a local file or nothing, never a download; a browser without one shows its own icon.
    /// </summary>
    private void SendIcon(MediaState media)
    {
        if (media.Bus.Length == 0)
        {
            lastIconKey = null;
            return;
        }

        var browser = MprisMap.IsBrowser(media.Bus);
        var key = browser ? media.Bus + "|" + media.ArtUrl : media.Bus;
        if (key == lastIconKey)
        {
            return;
        }

        lastIconKey = key;
        var label = media.App;
        string? path = null;
        if (browser && MprisMap.ArtworkPath(media.ArtUrl, ArtworkSize) is { } art)
        {
            path = art;
            label = "artwork";
        }
        else if (mpris.DesktopEntry(media.Bus) is { Length: > 0 } entry)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dirs = DesktopIcons.DataDirs(
                Environment.GetEnvironmentVariable("XDG_DATA_HOME"), Environment.GetEnvironmentVariable("XDG_DATA_DIRS"), home);
            path = DesktopIcons.IconForDesktopId(entry, dirs);
        }

        if (path is not null && LinuxMacroIcons.Render(path, IconSize, IconSize) is { } png)
        {
            blobs.Send(BlobKind.MediaIcon, label, png);
        }
    }

    private const int IconSize = 64;

    private static long? ArtworkSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The macro store reports from a pool thread, where an escape would end the process.
    private static void Guarded(Action report)
    {
        try
        {
            report();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // A lost macro list costs nothing; the session notices the dead socket itself.
        }
    }
}
