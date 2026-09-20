using System.Globalization;
using System.Runtime.InteropServices;
using Edgepad.Controls;
using Edgepad.Dispatch;
using Edgepad.Injection;
using Edgepad.Macros;
using Edgepad.Protocol;
using Edgepad.Trust;
using Windows.Networking.Sockets;

namespace Edgepad.Bluetooth;

/// <summary>
/// One connected phone: handshake, trust check, then a blocking read loop on its own thread that turns
/// each frame straight into input. There is no queue between the socket and SendInput. The laptop's own
/// state (volume, mute, brightness) goes back as STATE frames: a snapshot after the handshake, then every
/// audio change as it happens, so the phone's dials show what the laptop is really at.
/// </summary>
internal sealed class Session(
    StreamSocket socket,
    TrustStore trust,
    InputInjector injector,
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

    // The WinRT adapters read with partial-read semantics, so the default buffer returns as soon as
    // any bytes arrive — it saves per-byte calls without holding data back.
    private readonly Stream input = socket.InputStream.AsStreamForRead();
    private readonly Stream output = socket.OutputStream.AsStreamForWrite();
    private readonly byte[] sendBuffer = new byte[FrameCodec.MaxFrameLength];
    private readonly string address = socket.Information.RemoteHostName.RawName;

    // Audio notifications arrive on COM threads while the read loop sends PONGs: one writer at a time.
    private readonly Lock sendGate = new();
    private readonly List<IDisposable> watches = [];
    private bool disposed;
    private MediaState lastMedia = MediaSessions.Nothing;
    private string lastTimeline = "";

    public void Run()
    {
        var payload = new byte[FrameCodec.MaxFrameLength];
        try
        {
            if (ReadFrame(payload) is not Hello hello)
            {
                Log.Write($"Refused {address}: no valid HELLO");
                return;
            }

            if (hello.Version != ProtocolConstants.Version)
            {
                // Answered with this laptop's version, so the phone can say which side needs updating.
                Send(new HelloAck(ProtocolConstants.Version));
                Log.Write($"Refused {address}: it speaks protocol {hello.Version}, this laptop {ProtocolConstants.Version}");
                onStatus("Refused a phone from another release");
                return;
            }

            if (!trust.Admit(address))
            {
                Log.Write($"Refused {address}: this laptop trusts {trust.Trusted}");
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
        catch (Exception e) when (e is IOException or InvalidDataException or ObjectDisposedException or COMException)
        {
            Log.Write($"Session with {address} ended: {e.Message}");
        }
        finally
        {
            // Releases anything still held down: Alt in the middle of an app switch, a button mid-drag.
            injector.Dispose();
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

        Watch(speakers, ControlId.Volume);
        Watch(microphone, ControlId.MicLevel);
        watches.Add(media.Watch(state => Guarded(() => SendMedia(state))));

        // The macro names label buttons the phone only ever names by index, and the list changes while the
        // phone is connected: the owner adds one in the tray editor and expects the button, not a reason to
        // restart the app. Watching rather than asking once also covers the empty list, which is what tells
        // the phone to say "add some on the laptop" instead of drawing a grid that looks broken.
        watches.Add(macros.Watch(names => Guarded(() => Send(new Text((byte)TextKind.Macros, names)))));

        // Brightness changed on the laptop itself (keys, Windows' slider) reaches the phone as it does for audio.
        if (brightness.Watch(level => Guarded(() => SendBrightness(level))) is { } brightnessWatch)
        {
            watches.Add(brightnessWatch);
        }
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
                Send(new Text((byte)TextKind.MacroIcon, chunk));
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
        var watch = endpoint.Watch((percent, muted) => Guarded(() => SendState(control, (percent, muted))));
        if (watch is not null)
        {
            watches.Add(watch);
        }
    }

    private static void Guarded(Action send)
    {
        try
        {
            send();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or COMException)
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
        // Reached twice for a session the server replaced: once by the server, once by its own read loop.
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var watch in watches)
        {
            watch.Dispose();
        }

        watches.Clear();
        lock (sendGate)
        {
            input.Dispose();
            output.Dispose();
            socket.Dispose();
        }
    }
}
