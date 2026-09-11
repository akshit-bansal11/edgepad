using System.Buffers.Binary;
using System.Text;

namespace Edgepad.Protocol;

/// <summary>
/// Frame layout: one type byte, then a payload whose length is fixed by the type — except TEXT, whose
/// two header bytes (kind, length) say how much UTF-8 follows.
/// Little-endian. protocol/frames.txt holds the golden bytes both apps are tested against.
/// </summary>
internal static class FrameCodec
{
    public const int MaxTextBytes = 255;
    public const int TextHeaderLength = 2;
    public const int MaxFrameLength = 1 + TextHeaderLength + MaxTextBytes;

    /// <summary>What <see cref="PayloadLength"/> returns for TEXT: read the header, then as many bytes as it says.</summary>
    public const int LengthPrefixed = -2;

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
    private const byte TextType = 0x41;
    private const byte KeyType = 0x22;

    /// <summary>Payload length for a type byte, or -1 when the type is unknown.</summary>
    public static int PayloadLength(byte type) => type switch
    {
        HelloType => 5,
        HelloAckType or RunActionType => 1,
        PointerButtonType or ZoomType or SetValueType => 2,
        StateType or KeyType => 3,
        MoveType or ScrollType => 4,
        PingType or PongType => 8,
        TextType => LengthPrefixed,
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
            case Text f:
                var written = Encoding.UTF8.GetBytes(Truncate(f.Value), dest[(1 + TextHeaderLength)..]);
                dest[0] = TextType;
                dest[1] = f.Kind;
                dest[2] = (byte)written;
                return 1 + TextHeaderLength + written;
            case Key f:
                dest[0] = KeyType;
                BinaryPrimitives.WriteUInt16LittleEndian(dest[1..], f.Code);
                dest[3] = f.Down ? (byte)1 : (byte)0;
                return 4;
            default:
                throw new ArgumentException($"No encoding for {frame.GetType().Name}", nameof(frame));
        }
    }

    /// <summary>Decodes one frame. Anything malformed throws, and the caller drops the connection.</summary>
    public static Frame Decode(byte type, ReadOnlySpan<byte> payload)
    {
        var expected = PayloadLength(type);
        if (expected == LengthPrefixed)
        {
            return DecodeText(payload);
        }

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
            KeyType => new Key(BinaryPrimitives.ReadUInt16LittleEndian(payload), ReadFlag(payload[2])),
            _ => throw new InvalidDataException($"Unknown frame type 0x{type:x2}"),
        };
    }

    /// <summary>Cuts text to <see cref="MaxTextBytes"/> of UTF-8 without splitting a character.</summary>
    public static string Truncate(string value)
    {
        while (Encoding.UTF8.GetByteCount(value) > MaxTextBytes)
        {
            value = value[..^1];
        }

        return value;
    }

    private static Text DecodeText(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < TextHeaderLength || payload.Length != TextHeaderLength + payload[1])
        {
            throw new InvalidDataException("TEXT length does not match its header");
        }

        return new Text(payload[0], Encoding.UTF8.GetString(payload[TextHeaderLength..]));
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
