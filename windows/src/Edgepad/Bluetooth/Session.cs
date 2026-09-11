using System.Runtime.InteropServices;
using Edgepad.Dispatch;
using Edgepad.Injection;
using Edgepad.Protocol;
using Edgepad.Trust;
using Windows.Networking.Sockets;

namespace Edgepad.Bluetooth;

/// <summary>
/// One connected phone: handshake, trust check, then a blocking read loop on its own thread that turns
/// each frame straight into input. There is no queue between the socket and SendInput.
/// </summary>
internal sealed class Session(
    StreamSocket socket,
    TrustStore trust,
    InputInjector injector,
    Dispatcher dispatcher,
    Action<string> onStatus,
    Action<Session> onEnded) : IDisposable
{
    // The WinRT adapters read with partial-read semantics, so the default buffer returns as soon as
    // any bytes arrive — it saves per-byte calls without holding data back.
    private readonly Stream input = socket.InputStream.AsStreamForRead();
    private readonly Stream output = socket.OutputStream.AsStreamForWrite();
    private readonly byte[] sendBuffer = new byte[FrameCodec.MaxFrameLength];
    private readonly string address = socket.Information.RemoteHostName.RawName;

    public void Run()
    {
        var payload = new byte[FrameCodec.MaxFrameLength];
        try
        {
            if (ReadFrame(payload) is not Hello { Version: ProtocolConstants.Version })
            {
                Log.Write($"Refused {address}: no valid HELLO");
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

    private Frame ReadFrame(byte[] buffer)
    {
        var type = input.ReadByte();
        if (type < 0)
        {
            throw new EndOfStreamException("The phone closed the connection");
        }

        var length = FrameCodec.PayloadLength((byte)type);
        if (length < 0)
        {
            throw new InvalidDataException($"Unknown frame type 0x{type:x2}");
        }

        input.ReadExactly(buffer, 0, length);
        return FrameCodec.Decode((byte)type, buffer.AsSpan(0, length));
    }

    private void Send(Frame frame)
    {
        var length = FrameCodec.Encode(frame, sendBuffer);
        output.Write(sendBuffer, 0, length);
        output.Flush();
    }

    public void Dispose()
    {
        input.Dispose();
        output.Dispose();
        socket.Dispose();
    }
}
