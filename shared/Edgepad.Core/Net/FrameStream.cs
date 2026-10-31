using Edgepad.Protocol;

namespace Edgepad.Net;

/// <summary>
/// Whole frames over one phone's byte streams: a blocking read for the session's own thread, and a send any thread
/// may call. It closes neither stream; the session that opened them does, in the order its transport needs, which
/// is why it can reach them and the send lock.
/// </summary>
internal sealed class FrameStream(Stream input, Stream output)
{
    private readonly byte[] payload = new byte[FrameCodec.MaxFrameLength];
    private readonly byte[] sendBuffer = new byte[FrameCodec.MaxFrameLength];

    public Stream Input => input;

    public Stream Output => output;

    /// <summary>
    /// Held by every <see cref="Send"/>. State notifications arrive on their own threads (audio on COM threads, on
    /// Windows) while the read loop sends PONGs: one writer at a time. A caller holds it to keep several sends
    /// together, or to dispose the streams never under a writer's feet.
    /// </summary>
    public Lock SendGate { get; } = new();

    /// <summary>The next frame. Only the session's read thread calls this: every frame is read into the same buffer.</summary>
    public Frame ReadFrame()
    {
        var type = input.ReadByte();
        if (type < 0)
        {
            throw new EndOfStreamException("The phone closed the connection");
        }

        var length = FrameCodec.PayloadLength((byte)type);
        if (length == FrameCodec.LengthPrefixed)
        {
            input.ReadExactly(payload, 0, FrameCodec.TextHeaderLength);
            length = FrameCodec.TextHeaderLength + payload[1];
            input.ReadExactly(payload, FrameCodec.TextHeaderLength, payload[1]);
        }
        else if (length < 0)
        {
            throw new InvalidDataException($"Unknown frame type 0x{type:x2}");
        }
        else
        {
            input.ReadExactly(payload, 0, length);
        }

        return FrameCodec.Decode((byte)type, payload.AsSpan(0, length));
    }

    public void Send(Frame frame)
    {
        lock (SendGate)
        {
            var length = FrameCodec.Encode(frame, sendBuffer);
            output.Write(sendBuffer, 0, length);
            output.Flush();
        }
    }
}
