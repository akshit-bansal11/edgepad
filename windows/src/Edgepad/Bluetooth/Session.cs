using System.Runtime.InteropServices;
using Edgepad.Protocol;
using Windows.Networking.Sockets;

namespace Edgepad.Bluetooth;

/// <summary>One connected phone: handshake, then a blocking read loop on its own thread.</summary>
internal sealed class Session(StreamSocket socket, Action<string> onStatus, Action<Session> onEnded) : IDisposable
{
    // The WinRT adapters read with partial-read semantics, so the default buffer returns as soon as
    // any bytes arrive — it saves per-byte calls without holding data back.
    private readonly Stream input = socket.InputStream.AsStreamForRead();
    private readonly Stream output = socket.OutputStream.AsStreamForWrite();
    private readonly byte[] sendBuffer = new byte[FrameCodec.MaxFrameLength];
    private readonly string remote = socket.Information.RemoteHostName.DisplayName;

    public void Run()
    {
        var payload = new byte[FrameCodec.MaxFrameLength];
        try
        {
            if (ReadFrame(payload) is not Hello { Version: ProtocolConstants.Version })
            {
                Log.Write($"Refused {remote}: no valid HELLO");
                return;
            }

            Send(new HelloAck(ProtocolConstants.Version));
            Log.Write($"Connected {remote}");
            onStatus($"Connected to {remote}");

            while (true)
            {
                if (ReadFrame(payload) is Ping ping)
                {
                    Send(new Pong(ping.Time));
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ObjectDisposedException or COMException)
        {
            Log.Write($"Session with {remote} ended: {e.Message}");
        }
        finally
        {
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
