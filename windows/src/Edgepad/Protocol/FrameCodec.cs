using System.Buffers.Binary;

namespace Edgepad.Protocol;

/// <summary>
/// Frame layout: one type byte, then a payload whose length is fixed by the type.
/// Little-endian. protocol/frames.txt holds the golden bytes both apps are tested against.
/// </summary>
internal static class FrameCodec
{
    public const int MaxFrameLength = 9;

    private const byte HelloType = 0x01;
    private const byte HelloAckType = 0x02;
    private const byte MoveType = 0x10;
    private const byte PointerButtonType = 0x11;
    private const byte ScrollType = 0x12;
    private const byte ZoomType = 0x13;
    private const byte RunActionType = 0x20;
    private const byte SetValueType = 0x21;
    private const byte PingType = 0x30;
    private const byte PongType = 0x31;
    private const byte StateType = 0x40;

    /// <summary>Payload length for a type byte, or -1 when the type is unknown.</summary>
    public static int PayloadLength(byte type) => type switch
    {
        HelloType => 5,
        HelloAckType or RunActionType => 1,
        PointerButtonType or ZoomType or SetValueType => 2,
        StateType => 3,
        MoveType or ScrollType => 4,
        PingType or PongType => 8,
        _ => -1,
    };

    public static byte[] Encode(Frame frame)
    {
        var buffer = new byte[MaxFrameLength];
        return buffer[..Encode(frame, buffer)];
    }

    /// <summary>Writes the frame into <paramref name="dest"/> and returns the number of bytes written.</summary>
    public static int Encode(Frame frame, Span<byte> dest)
    {
        switch (frame)
        {
            case Hello f:
                dest[0] = HelloType;
                ProtocolConstants.Magic.CopyTo(dest[1..]);
                dest[5] = f.Version;
                return 6;
            case HelloAck f:
                return WriteByte(dest, HelloAckType, f.Version);
            case Move f:
                return WritePair(dest, MoveType, f.Dx, f.Dy);
            case PointerButton f:
                dest[0] = PointerButtonType;
                dest[1] = f.Id;
                dest[2] = f.Down ? (byte)1 : (byte)0;
                return 3;
            case Scroll f:
                return WritePair(dest, ScrollType, f.Dx, f.Dy);
            case Zoom f:
                dest[0] = ZoomType;
                BinaryPrimitives.WriteInt16LittleEndian(dest[1..], f.Delta);
                return 3;
            case RunAction f:
                return WriteByte(dest, RunActionType, f.Id);
            case SetValue f:
                dest[0] = SetValueType;
                dest[1] = f.Control;
                dest[2] = f.Value;
                return 3;
            case Ping f:
                return WriteTime(dest, PingType, f.Time);
            case Pong f:
                return WriteTime(dest, PongType, f.Time);
            case StateReport f:
                dest[0] = StateType;
                dest[1] = f.Control;
                dest[2] = f.Value;
                dest[3] = f.Flags;
                return 4;
            default:
                throw new ArgumentException($"No encoding for {frame.GetType().Name}", nameof(frame));
        }
    }

    /// <summary>Decodes one frame. Anything malformed throws, and the caller drops the connection.</summary>
    public static Frame Decode(byte type, ReadOnlySpan<byte> payload)
    {
        var expected = PayloadLength(type);
        if (expected < 0)
        {
            throw new InvalidDataException($"Unknown frame type 0x{type:x2}");
        }

        if (payload.Length != expected)
        {
            throw new InvalidDataException($"Frame 0x{type:x2} needs {expected} payload bytes, got {payload.Length}");
        }

        return type switch
        {
            HelloType => DecodeHello(payload),
            HelloAckType => new HelloAck(payload[0]),
            MoveType => new Move(ReadInt16(payload), ReadInt16(payload[2..])),
            PointerButtonType => new PointerButton(payload[0], ReadFlag(payload[1])),
            ScrollType => new Scroll(ReadInt16(payload), ReadInt16(payload[2..])),
            ZoomType => new Zoom(ReadInt16(payload)),
            RunActionType => new RunAction(payload[0]),
            SetValueType => new SetValue(payload[0], payload[1]),
            PingType => new Ping(BinaryPrimitives.ReadInt64LittleEndian(payload)),
            PongType => new Pong(BinaryPrimitives.ReadInt64LittleEndian(payload)),
            StateType => new StateReport(payload[0], payload[1], payload[2]),
            _ => throw new InvalidDataException($"Unknown frame type 0x{type:x2}"),
        };
    }

    private static Hello DecodeHello(ReadOnlySpan<byte> payload)
    {
        if (!payload[..4].SequenceEqual(ProtocolConstants.Magic))
        {
            throw new InvalidDataException("HELLO without the Edgepad magic");
        }

        return new Hello(payload[4]);
    }

    private static bool ReadFlag(byte value) => value switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException($"Flag byte must be 0 or 1, got {value}"),
    };

    private static short ReadInt16(ReadOnlySpan<byte> source) => BinaryPrimitives.ReadInt16LittleEndian(source);

    private static int WriteByte(Span<byte> dest, byte type, byte value)
    {
        dest[0] = type;
        dest[1] = value;
        return 2;
    }

    private static int WritePair(Span<byte> dest, byte type, short a, short b)
    {
        dest[0] = type;
        BinaryPrimitives.WriteInt16LittleEndian(dest[1..], a);
        BinaryPrimitives.WriteInt16LittleEndian(dest[3..], b);
        return 5;
    }

    private static int WriteTime(Span<byte> dest, byte type, long time)
    {
        dest[0] = type;
        BinaryPrimitives.WriteInt64LittleEndian(dest[1..], time);
        return 9;
    }
}
