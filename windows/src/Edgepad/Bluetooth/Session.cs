using System.Globalization;
using System.Runtime.InteropServices;
using Edgepad.Controls;
using Edgepad.Dispatch;
using Edgepad.Gamepad;
using Edgepad.Injection;
using Edgepad.Macros;
using Edgepad.Protocol;
using Edgepad.Trust;
using Windows.Networking.Sockets;

namespace Edgepad.Bluetooth;

/// <summary>
/// One connected phone: handshake, trust check (asking the owner, through askTrust, about a phone while none
/// is trusted), then a blocking read loop on its own thread that turns
/// each frame straight into input. There is no queue between the socket and SendInput. The laptop's own
/// state (volume, mute, brightness) goes back as STATE frames: a snapshot after the handshake, then every
/// audio change as it happens, so the phone's dials show what the laptop is really at.
/// </summary>
internal sealed class Session(
    StreamSocket socket,
    TrustStore trust,
    Func<string, bool> askTrust,
    InputInjector injector,
    VirtualPad pad,
    Dispatcher dispatcher,
    AudioEndpoint speakers,
    AudioEndpoint microphone,
    BrightnessControl brightness,
    MediaSessions media,
    DisplayModes display,
    MacroStore macros,
    Action<string> onStatus,
    Action<Session> onEnded) : IDisposable
{
    private const byte MutedFlag = 1;
    private const byte PlayingFlag = 1;

    /// <summary>
    /// How long an open socket may go without a HELLO. The read has no timeout of its own, so a paired device
    /// that connects and says nothing held this thread, and before any phone is trusted the one session the
    /// laptop serves, for as long as the link stayed up.
    /// </summary>
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);

    // The WinRT adapters read with partial-read semantics, so the default buffer returns as soon as
    // any bytes arrive — it saves per-byte calls without holding data back.
    private readonly Stream input = socket.InputStream.AsStreamForRead();
    private readonly Stream output = socket.OutputStream.AsStreamForWrite();
    private readonly byte[] sendBuffer = new byte[FrameCodec.MaxFrameLength];
    private readonly string address = socket.Information.RemoteHostName.RawName;

    // Audio notifications arrive on COM threads while the read loop sends PONGs: one writer at a time.
    private readonly Lock sendGate = new();

    // Dispose runs from the server's thread, a timer's or the read loop's, while the read loop may still be
    // adding watches: both the list and the flag are only touched under this.
    private readonly Lock watchGate = new();
    private readonly List<IDisposable> watches = [];
    private bool disposed;
    private MediaState lastMedia = MediaSessions.Nothing;
    private string lastTimeline = "";

    public void Run()
    {
        var payload = new byte[FrameCodec.MaxFrameLength];
        try
        {
            Frame first;
            using (new System.Threading.Timer(_ => GiveUpOnHello(), null, HelloTimeout, Timeout.InfiniteTimeSpan))
            {
                first = ReadFrame(payload);
            }

            if (first is not Hello hello)
            {
                Log.Write($"Refused {address}: no valid HELLO");
                return;
            }

            if (hello.Version != ProtocolConstants.Version)
            {
                // Answered with this laptop's version, so the phone can say which side needs updating.
                Send(new HelloAck(ProtocolConstants.Version));
                Log.Write($"Refused {address}: it speaks protocol {hello.Version}, this laptop {ProtocolConstants.Version}");
                onStatus($"Refused a phone on protocol {hello.Version}; this laptop is on {ProtocolConstants.Version}");
                return;
            }

            // HELLO_ACK waits on the owner's answer, and a refusal closes the socket without one: the same
            // bytes a phone it did not trust has always been sent, so no phone needs to learn anything new.
            if (!trust.Admit(address, () => askTrust(address)))
            {
                Log.Write($"Refused {address}: this laptop trusts {trust.Trusted ?? "no phone yet"}");
                onStatus("Refused a phone it does not trust");
                return;
            }

            Send(new HelloAck(ProtocolConstants.Version));
            Log.Write($"Connected {address}");
            onStatus($"Connected to {address}");
            ReportState();

            while (true)
            {
                var frame = ReadFrame(payload);
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
        finally
        {
            // Releases anything still held down: Alt in the middle of an app switch, a button mid-drag.
            injector.Dispose();

            // And unplugs the virtual controller, which is the same bug one layer along: a phone that drops
            // mid-game would otherwise leave a pad plugged into Windows with a stick still pushed forward,
            // and nothing left alive to centre it.
            pad.Dispose();
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

    private void GiveUpOnHello()
    {
        Log.Write($"Refused {address}: no HELLO within {(int)HelloTimeout.TotalSeconds} s");

        // Closing the socket is what ends the blocked read; the read loop then finishes the session as usual.
        Dispose();
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
                lock (sendGate)
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

    private Frame ReadFrame(byte[] buffer)
    {
        var type = input.ReadByte();
        if (type < 0)
        {
            throw new EndOfStreamException("The phone closed the connection");
        }

        var length = FrameCodec.PayloadLength((byte)type);
        if (length == FrameCodec.LengthPrefixed)
        {
            input.ReadExactly(buffer, 0, FrameCodec.TextHeaderLength);
            length = FrameCodec.TextHeaderLength + buffer[1];
            input.ReadExactly(buffer, FrameCodec.TextHeaderLength, buffer[1]);
        }
        else if (length < 0)
        {
            throw new InvalidDataException($"Unknown frame type 0x{type:x2}");
        }
        else
        {
            input.ReadExactly(buffer, 0, length);
        }

        return FrameCodec.Decode((byte)type, buffer.AsSpan(0, length));
    }

    private void Send(Frame frame)
    {
        lock (sendGate)
        {
            var length = FrameCodec.Encode(frame, sendBuffer);
            output.Write(sendBuffer, 0, length);
            output.Flush();
        }
    }

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

        foreach (var watch in ending)
        {
            watch.Dispose();
        }

        // The socket first, and outside the send lock. A Send blocked in a write to a phone that has stopped
        // reading holds that lock for as long as the write blocks; waiting for it here stalled the server's
        // thread, and with it the new phone's session. Closing the socket aborts the write, the Send throws
        // and lets go, and only then are the streams disposed, never under a writer's feet.
        socket.Dispose();
        lock (sendGate)
        {
            input.Dispose();
            try
            {
                output.Dispose();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or COMException or OperationCanceledException)
            {
                // Its buffer may still hold the aborted write, which disposing tries to flush to a closed
                // socket. Those bytes were never going to arrive.
            }
        }
    }
}
