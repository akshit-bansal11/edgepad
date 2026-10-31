using System.Globalization;
using Edgepad.Macros;
using Edgepad.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// Tells the phone what the laptop is doing, for the life of one session: the volume, microphone and brightness
/// dials, what is playing and the macro names. Linux has no change events
/// for these that every setup shares, so it polls twice a second like the Mac app and sends only what changed;
/// a change the laptop makes itself (its own volume keys) reaches the phone's dials within half a second. The
/// polling runs on a thread of its own because each read starts a process or makes a D-Bus call. A send that fails
/// means the socket is gone, which the session's reader notices, so this just stops.
/// </summary>
internal sealed class StateReporter : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(2);

    private const byte MutedFlag = 1;
    private const byte PlayingFlag = 1;

    private readonly Audio speakers;
    private readonly Audio microphone;
    private readonly Backlight backlight;
    private readonly Mpris mpris;
    private readonly MacroStore macros;
    private readonly Action<Frame> send;
    private readonly CancellationTokenSource stopping = new();
    private readonly Thread thread;

    private Level? lastVolume;
    private Level? lastMic;
    private int? lastBrightness;
    private MediaState? lastMedia;
    private string? lastTimeline;

    public StateReporter(Audio speakers, Audio microphone, Backlight backlight, Mpris mpris, MacroStore macros, Action<Frame> send)
    {
        this.speakers = speakers;
        this.microphone = microphone;
        this.backlight = backlight;
        this.mpris = mpris;
        this.macros = macros;
        this.send = send;
        thread = new Thread(Run) { IsBackground = true, Name = "edgepad-state" };
        thread.Start();
    }

    /// <summary>The text of a timeline: "seconds in/seconds long", empty when the player gives no length.</summary>
    public static string Timeline(MediaState state) => state.DurationSeconds > 0
        ? string.Create(CultureInfo.InvariantCulture, $"{state.PositionSeconds}/{state.DurationSeconds}")
        : "";

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
            // refresh-rate list is empty because Linux has no shared way to switch modes; the phone's dial then has
            // no travel.
            send(new Text((byte)TextKind.RefreshRates, ""));

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

        var media = mpris.Poll();
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

        lastMedia = media;
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
