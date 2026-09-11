using System.Globalization;
using System.Runtime.InteropServices;
using Edgepad.Controls;
using Edgepad.Dispatch;
using Edgepad.Injection;
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
    MediaSessions media,
    Action<string> onStatus,
    Action<Session> onEnded) : IDisposable
{
    private const byte MutedFlag = 1;
    private const byte PlayingFlag = 1;
    private const byte NowPlayingText = 0;
    private const byte AppText = 1;
    private const byte TimelineText = 2;

    // The WinRT adapters read with partial-read semantics, so the default buffer returns as soon as
    // any bytes arrive — it saves per-byte calls without holding data back.
    private readonly Stream input = socket.InputStream.AsStreamForRead();
    private readonly Stream output = socket.OutputStream.AsStreamForWrite();
    private readonly byte[] sendBuffer = new byte[FrameCodec.MaxFrameLength];
    private readonly string address = socket.Information.RemoteHostName.RawName;

    // Audio notifications arrive on COM threads while the read loop sends PONGs: one writer at a time.
    private readonly Lock sendGate = new();
    private readonly List<IDisposable> watches = [];
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
        if (BrightnessControl.Read() is { } brightness)
        {
            SendBrightness(brightness);
        }

        Watch(speakers, ControlId.Volume);
        Watch(microphone, ControlId.MicLevel);
        watches.Add(media.Watch(state => Guarded(() => SendMedia(state))));

        // Brightness changed on the laptop itself (keys, Windows' slider) reaches the phone as it does for audio.
        if (BrightnessControl.Watch(level => Guarded(() => SendBrightness(level))) is { } brightnessWatch)
        {
            watches.Add(brightnessWatch);
        }
    }

    private void SendBrightness(int level) =>
        Send(new StateReport((byte)ControlId.Brightness, (byte)Math.Clamp(level, 0, 100), 0));

    private void SendMedia(MediaState state)
    {
        // Text only when it changes; the position every time, since it is what moves.
        if (state.NowPlaying != lastMedia.NowPlaying)
        {
            Send(new Text(NowPlayingText, state.NowPlaying));
        }

        if (state.App != lastMedia.App)
        {
            Send(new Text(AppText, state.App));
        }

        // Seconds in and the length, so the phone can show 1:24 of 3:47; empty when the player has no timeline.
        var timeline = state.DurationSeconds > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{state.PositionSeconds}/{state.DurationSeconds}")
            : "";
        if (timeline != lastTimeline)
        {
            Send(new Text(TimelineText, timeline));
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
