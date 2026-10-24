using System.Globalization;
using System.Runtime.InteropServices;
using Edgepad.Annotation;
using Edgepad.Controls;
using Edgepad.Dispatch;
using Edgepad.Gamepad;
using Edgepad.Injection;
using Edgepad.Macros;
using Edgepad.Net;
using Edgepad.Protocol;
using Edgepad.Trust;
using Microsoft.Win32;
using Windows.Networking.Sockets;

namespace Edgepad.Bluetooth;

/// <summary>
/// One connected phone: handshake, trust check (asking the owner, through askTrust, about a phone not trusted
/// yet), the server's word that it may take over (onAdmitted), then a blocking read loop on its own thread that
/// turns each frame straight into input. There is no queue between the socket and SendInput. The laptop's own
/// state (volume, mute, brightness) goes back as STATE frames: a snapshot after the handshake, then every
/// audio change as it happens, so the phone's dials show what the laptop is really at.
/// </summary>
internal sealed class Session(
    StreamSocket socket,
    TrustStore trust,
    PinStore pin,
    Func<string, CancellationToken, bool> askTrust,
    InputInjector injector,
    VirtualPad pad,
    Dispatcher dispatcher,
    Annotator annotator,
    AudioEndpoint speakers,
    AudioEndpoint microphone,
    BrightnessControl brightness,
    MediaSessions media,
    DisplayModes display,
    MacroStore macros,
    Action<string> onStatus,
    Func<Session, bool> onAdmitted,
    Action<Session> onEnded) : IDisposable
{
    private const byte MutedFlag = 1;
    private const byte PlayingFlag = 1;

    // The WinRT adapters read with partial-read semantics, so the default buffer returns as soon as
    // any bytes arrive — it saves per-byte calls without holding data back.
    private readonly FrameStream frames = new(socket.InputStream.AsStreamForRead(), socket.OutputStream.AsStreamForWrite());
    private readonly string address = socket.Information.RemoteHostName.RawName;

    // Dispose runs from the server's thread, a timer's or the read loop's, while the read loop may still be
    // adding watches: both the list and the flag are only touched under this.
    private readonly Lock watchGate = new();
    private readonly List<IDisposable> watches = [];

    // Cancelled by Dispose, so a trust question open for this session, or about to be put, is withdrawn.
    private readonly CancellationTokenSource ended = new();
    private bool disposed;
    private MediaState lastMedia = MediaSessions.Nothing;
    private string lastTimeline = "";

    /// <summary>The phone's Bluetooth address, as the log and the status line name it.</summary>
    public string Address => address;

    public void Run()
    {
        try
        {
            // A refusal comes back here and the finally below closes the socket; a phone gone silent mid-handshake
            // is ended through Dispose, which aborts the blocked read.
            if (!Handshake.Run(frames, address, trust, pin, askTrust, () => onAdmitted(this), onStatus, Log.Write, Dispose, ended.Token))
            {
                return;
            }

            ReportState();

            while (true)
            {
                var frame = frames.ReadFrame();
                if (frame is Ping ping)
                {
                    Send(new Pong(ping.Time));
                }
                else if (frame is Text { Kind: (byte)TextKind.WantIcons })
                {
                    // Answered here rather than in the dispatcher, for the same reason PONG is: the reply
                    // goes back down this socket, and the dispatcher deliberately holds no reference to it.
                    SendIcons();
                }
                else
                {
                    dispatcher.Handle(frame);
                }
            }
        }
        // A read or write the socket's closing aborted can surface as a cancellation from the WinRT stream
        // adapters, and the HELLO timeout and a replacing phone both close the socket under a blocked read.
        catch (Exception e) when (e is IOException or InvalidDataException or ObjectDisposedException or COMException
            or OperationCanceledException)
        {
            Log.Write($"Session with {address} ended: {e.Message}");
        }
        // Anything else is a bug on this side or a device API failing in a way nobody listed. It ends this one
        // session, with the whole exception in the log, rather than the tray app.
        catch (Exception e)
        {
            Log.Write($"Session with {address} failed: {e}");
        }
        finally
        {
            // Releases anything still held down: Alt in the middle of an app switch, a button mid-drag.
            injector.Dispose();

            // And unplugs the virtual controller, which is the same bug one layer along: a phone that drops
            // mid-game would otherwise leave a pad plugged into Windows with a stick still pushed forward,
            // and nothing left alive to centre it.
            pad.Dispose();

            // And takes the annotation layer down with its strokes: a phone that vanishes mid-drawing must not
            // leave ink over the owner's screen with nothing left alive that could end it.
            annotator.Dispose();
            if (dispatcher.Dropped > 0 || injector.RefusedBatches > 0)
            {
                Log.Write($"Session with {address}: {dispatcher.Dropped} frames dropped, "
                    + $"{injector.RefusedBatches} input batches refused by Windows (an elevated window had focus?)");
            }

            Dispose();
            onEnded(this);
        }
    }

    private void ReportState()
    {
        SendState(ControlId.Volume, speakers.Read());
        SendState(ControlId.MicLevel, microphone.Read());
        if (brightness.Read() is { } level)
        {
            SendBrightness(level);
        }

        ReportRefreshRates();
        ReportPad();

        // Only what this build can do: the phone hides a feature that is not named, so naming one that is
        // not there would be a button that quietly does nothing.
        Send(new Text((byte)TextKind.Features, $"{Feature.Annotate}/{Feature.Capture}"));
        ReportScreen();

        Watch(speakers, ControlId.Volume);
        Watch(microphone, ControlId.MicLevel);
        Keep(media.Watch(state => Guarded(() => SendMedia(state))));

        // The macro names label buttons the phone only ever names by index, and the list changes while the
        // phone is connected: the owner adds one in the tray editor and expects the button, not a reason to
        // restart the app. Watching rather than asking once also covers the empty list, which is what tells
        // the phone to say "add some on the laptop" instead of drawing a grid that looks broken.
        Keep(macros.Watch(names => Guarded(() => Send(new Text((byte)TextKind.Macros, names)))));

        // Brightness changed on the laptop itself (keys, Windows' slider) reaches the phone as it does for audio.
        Keep(brightness.Watch(level => Guarded(() => SendBrightness(level))));
    }

    /// <summary>
    /// Holds a watch until the session ends. One that arrives after the end, because another thread disposed
    /// the session while this one was still setting it up, is released at once: nothing else ever would, and it
    /// would go on writing reports to a closed socket for the life of the app.
    /// </summary>
    private void Keep(IDisposable? watch)
    {
        if (watch is null)
        {
            return;
        }

        lock (watchGate)
        {
            if (!disposed)
            {
                watches.Add(watch);
                return;
            }
        }

        watch.Dispose();
    }

    /// <summary>
    /// The rates this display offers and which one is in force. The list goes first because the level is an
    /// index into it, though the phone corrects itself either way if they arrive the other way round.
    /// A display that offers nothing sends an empty list, and the phone's dial then has no travel.
    /// </summary>
    private void ReportRefreshRates()
    {
        display.Refresh();
        Send(new Text((byte)TextKind.RefreshRates, string.Join('/', display.Rates)));
        if (display.Current >= 0)
        {
            Send(new StateReport((byte)ControlId.RefreshRate, (byte)display.Current, 0));
        }
    }

    /// <summary>
    /// The primary display's size, which the phone shapes its drawing surface to, now and whenever it changes
    /// (a resolution switch, a docked monitor taking over as primary), and where SIGN_SAVE's answer goes.
    /// </summary>
    private void ReportScreen()
    {
        // From this thread when there is no ink, like the pad's answer; otherwise from the export's own thread,
        // where the annotator guards it, because a dead socket is the read loop's to notice.
        annotator.SignStatusReply = status => Send(new Text((byte)TextKind.SignStatus, status));
        SendScreenSize();

        // SystemEvents may raise this on the UI thread. The write is moved off it, because a write to a phone that
        // has stopped reading blocks, and the tray must not freeze with it.
        EventHandler changed = (_, _) => _ = Task.Run(() => Guarded(SendScreenSize));
        SystemEvents.DisplaySettingsChanged += changed;
        Keep(new Subscription(() => SystemEvents.DisplaySettingsChanged -= changed));
    }

    private void SendScreenSize()
    {
        var screen = Annotator.PrimaryBounds();
        Send(new Text((byte)TextKind.ScreenSize, string.Create(CultureInfo.InvariantCulture, $"{screen.Width}/{screen.Height}")));
    }

    /// <summary>
    /// Whether this laptop can offer a virtual controller, and where the answers to PAD_ATTACH and
    /// PAD_DETACH go from here on. Both land on the read loop's thread, so they are written like a PONG and
    /// not like a watcher's report: a dead socket here should end the session, not be swallowed as a lost
    /// update. The opening one is sent before the phone can ask, so it can hide the pad rather than offer a
    /// button that quietly does nothing.
    /// </summary>
    private void ReportPad()
    {
        dispatcher.PadStatusReply = status => Send(new Text((byte)TextKind.PadStatus, status));
        Send(new Text((byte)TextKind.PadStatus, pad.Probe()));
    }

    /// <summary>
    /// Every macro icon this laptop can read, in pieces. Runs on the read loop, which means no input frame
    /// is read while it goes out — and that is the point of the phone asking rather than being pushed at:
    /// it asks when it opens the macro grid, which is a screen with no trackpad on it, so the pause is in a
    /// moment where nothing is being pointed at. An icon that cannot be read is simply not sent, and its
    /// button keeps the label it already had.
    /// </summary>
    private void SendIcons()
    {
        var current = macros.Macros;
        for (var slot = 0; slot < current.Count; slot++)
        {
            if (MacroIcons.Png(current[slot]) is not { } png)
            {
                continue;
            }

            foreach (var chunk in MacroIcons.Chunks(slot, png))
            {
                // A save in the tray editor swaps the list and then sends it from another thread. Checked
                // under the send lock, no chunk of the old list can follow the new one to the phone, where
                // it would land on whatever macro moved into its slot. The phone asks again for the new list.
                lock (frames.SendGate)
                {
                    if (!ReferenceEquals(macros.Macros, current))
                    {
                        return;
                    }

                    Send(new Text((byte)TextKind.MacroIcon, chunk));
                }
            }
        }
    }

    private void SendBrightness(int level) =>
        Send(new StateReport((byte)ControlId.Brightness, (byte)Math.Clamp(level, 0, 100), 0));

    private void SendMedia(MediaState state)
    {
        // Text only when it changes; the position every time, since it is what moves.
        if (state.NowPlaying != lastMedia.NowPlaying)
        {
            Send(new Text((byte)TextKind.NowPlaying, state.NowPlaying));
        }

        if (state.App != lastMedia.App)
        {
            Send(new Text((byte)TextKind.App, state.App));
        }

        // Seconds in and the length, so the phone can show 1:24 of 3:47; empty when the player has no timeline.
        var timeline = state.DurationSeconds > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{state.PositionSeconds}/{state.DurationSeconds}")
            : "";
        if (timeline != lastTimeline)
        {
            Send(new Text((byte)TextKind.Timeline, timeline));
            lastTimeline = timeline;
        }

        lastMedia = state;
        Send(new StateReport((byte)ControlId.MediaPosition, (byte)state.PositionPercent, state.Playing ? PlayingFlag : (byte)0));
    }

    private void Watch(AudioEndpoint endpoint, ControlId control)
    {
        Keep(endpoint.Watch((percent, muted) => Guarded(() => SendState(control, (percent, muted)))));
    }

    private static void Guarded(Action send)
    {
        try
        {
            send();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or COMException or OperationCanceledException)
        {
            // The read loop notices the dead socket and ends the session; a lost report costs nothing.
        }
    }

    private void SendState(ControlId control, (int Percent, bool Muted)? state)
    {
        if (state is { } s)
        {
            Send(new StateReport((byte)control, (byte)s.Percent, s.Muted ? MutedFlag : (byte)0));
        }
    }

    private void Send(Frame frame) => frames.Send(frame);

    public void Dispose()
    {
        // Reached twice for a session the server replaced: once by the server, once by its own read loop, and
        // those two can race each other and the loop's own setup.
        IDisposable[] ending;
        lock (watchGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            ending = [.. watches];
            watches.Clear();
        }

        ended.Cancel();

        foreach (var watch in ending)
        {
            watch.Dispose();
        }

        // The socket first, and outside the send lock. A Send blocked in a write to a phone that has stopped
        // reading holds that lock for as long as the write blocks; waiting for it here stalled the server's
        // thread, and with it the new phone's session. Closing the socket aborts the write, the Send throws
        // and lets go, and only then are the streams disposed, never under a writer's feet.
        socket.Dispose();
        lock (frames.SendGate)
        {
            frames.Input.Dispose();
            try
            {
                frames.Output.Dispose();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or COMException or OperationCanceledException)
            {
                // Its buffer may still hold the aborted write, which disposing tries to flush to a closed
                // socket. Those bytes were never going to arrive.
            }
        }

        // Already cancelled, so a question still reading the token sees that, never a live one.
        ended.Dispose();
    }
}
