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
    private const byte PinRequestType = 0x03;
    private const byte PinAnswerType = 0x04;
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
    private const byte PadStateType = 0x23;
    private const byte PenType = 0x24;
    private const byte FileBeginType = 0x50;
    private const byte FileChunkType = 0x51;
    private const byte FileEndType = 0x52;
    private const byte FileResultType = 0x53;
    private const byte BlobBeginType = 0x54;
    private const byte BlobChunkType = 0x55;
    private const byte BlobEndType = 0x56;

    /// <summary>FILE_BEGIN's size field, ahead of the name in the length-prefixed part.</summary>
    private const int FileSizeBytes = 4;

    /// <summary>The most one FILE_CHUNK carries: the 255 bytes a TEXT does, so no frame grows past <see cref="MaxFrameLength"/>.</summary>
    public const int MaxChunkBytes = MaxTextBytes;

    /// <summary>The most of a name one FILE_BEGIN carries: what is left of the 255 bytes after the size.</summary>
    public const int MaxFileNameBytes = MaxTextBytes - FileSizeBytes;

    public const int Sha256Bytes = 32;

    /// <summary>
    /// Payload length for a type byte, <see cref="LengthPrefixed"/> for TEXT and the file and blob BEGIN and CHUNK frames, or -1 when the
    /// type is unknown. The two file frames borrow TEXT's header shape, an id and then a length, so a reader frames
    /// them without knowing what they are.
    /// <para>
    /// PAD_STATE is the longest fixed payload at 12 bytes, so 13 on the wire — well inside
    /// <see cref="MaxFrameLength"/>, which a 255-byte TEXT still sets at 258. No buffer had to move to let
    /// the pad in.
    /// </para>
    /// </summary>
    public static int PayloadLength(byte type) => type switch
    {
        HelloType => 5,
        HelloAckType or RunActionType or FileResultType => 1,
        PinAnswerType => Pin.Length,
        PointerButtonType or ZoomType or SetValueType => 2,
        StateType or KeyType or PinRequestType => 3,
        MoveType or ScrollType => 4,
        PingType or PongType => 8,
        PadStateType => 12,
        PenType => 5,
        FileEndType or BlobEndType => Sha256Bytes,
        TextType or FileBeginType or FileChunkType or BlobBeginType or BlobChunkType => LengthPrefixed,
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
            // The XINPUT_GAMEPAD field order exactly: wButtons, bLeftTrigger, bRightTrigger, then the four
            // thumb axes, little-endian like everything else. This side only ever decodes PAD_STATE, but it
            // encodes here too, because the fixture makes both codecs prove both directions on every line.
            case PadState f:
                dest[0] = PadStateType;
                BinaryPrimitives.WriteUInt16LittleEndian(dest[1..], f.Buttons);
                dest[3] = f.Lt;
                dest[4] = f.Rt;
                BinaryPrimitives.WriteInt16LittleEndian(dest[5..], f.Lx);
                BinaryPrimitives.WriteInt16LittleEndian(dest[7..], f.Ly);
                BinaryPrimitives.WriteInt16LittleEndian(dest[9..], f.Rx);
                BinaryPrimitives.WriteInt16LittleEndian(dest[11..], f.Ry);
                return 13;
            case PinRequest f:
                dest[0] = PinRequestType;
                dest[1] = f.AttemptsLeft;
                BinaryPrimitives.WriteUInt16LittleEndian(dest[2..], f.LockedSeconds);
                return 4;
            case Pen f:
                dest[0] = PenType;
                BinaryPrimitives.WriteUInt16LittleEndian(dest[1..], f.X);
                BinaryPrimitives.WriteUInt16LittleEndian(dest[3..], f.Y);
                dest[5] = f.State;
                return 6;
            // TEXT's shape: the type id, then one length byte counting the size and the name together.
            case FileBegin f:
                var nameBytes = Encoding.UTF8.GetBytes(Truncate(f.Name, MaxFileNameBytes), dest[(1 + TextHeaderLength + FileSizeBytes)..]);
                dest[0] = FileBeginType;
                dest[1] = f.Type;
                dest[2] = (byte)(FileSizeBytes + nameBytes);
                BinaryPrimitives.WriteUInt32LittleEndian(dest[(1 + TextHeaderLength)..], f.Size);
                return 1 + TextHeaderLength + FileSizeBytes + nameBytes;
            case FileChunk f:
                if (f.Data.Length > MaxChunkBytes)
                {
                    throw new ArgumentException($"A chunk is at most {MaxChunkBytes} bytes", nameof(frame));
                }

                dest[0] = FileChunkType;
                dest[1] = f.Seq;
                dest[2] = (byte)f.Data.Length;
                f.Data.CopyTo(dest[(1 + TextHeaderLength)..]);
                return 1 + TextHeaderLength + f.Data.Length;
            case FileEnd f:
                if (f.Sha256.Length != Sha256Bytes)
                {
                    throw new ArgumentException($"A SHA-256 is {Sha256Bytes} bytes", nameof(frame));
                }

                dest[0] = FileEndType;
                f.Sha256.CopyTo(dest[1..]);
                return 1 + Sha256Bytes;
            // The same three shapes as the file frames, under their own type bytes.
            case BlobBegin f:
                var labelBytes = Encoding.UTF8.GetBytes(Truncate(f.Label, MaxFileNameBytes), dest[(1 + TextHeaderLength + FileSizeBytes)..]);
                dest[0] = BlobBeginType;
                dest[1] = f.Kind;
                dest[2] = (byte)(FileSizeBytes + labelBytes);
                BinaryPrimitives.WriteUInt32LittleEndian(dest[(1 + TextHeaderLength)..], f.Size);
                return 1 + TextHeaderLength + FileSizeBytes + labelBytes;
            case BlobChunk f:
                if (f.Data.Length > MaxChunkBytes)
                {
                    throw new ArgumentException($"A chunk is at most {MaxChunkBytes} bytes", nameof(frame));
                }

                dest[0] = BlobChunkType;
                dest[1] = f.Seq;
                dest[2] = (byte)f.Data.Length;
                f.Data.CopyTo(dest[(1 + TextHeaderLength)..]);
                return 1 + TextHeaderLength + f.Data.Length;
            case BlobEnd f:
                if (f.Sha256.Length != Sha256Bytes)
                {
                    throw new ArgumentException($"A SHA-256 is {Sha256Bytes} bytes", nameof(frame));
                }

                dest[0] = BlobEndType;
                f.Sha256.CopyTo(dest[1..]);
                return 1 + Sha256Bytes;
            case FileResult f:
                return WriteByte(dest, FileResultType, f.Status);
            // Each digit goes out as its value 0..9, not its ASCII code, so these bytes can never be read as text.
            case PinAnswer f:
                dest[0] = PinAnswerType;
                for (var i = 0; i < Pin.Length; i++)
                {
                    dest[1 + i] = (byte)(f.Digits.Digits[i] - '0');
                }

                return 1 + Pin.Length;
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
            return DecodePrefixed(type, payload);
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
            KeyType => new Key(BinaryPrimitives.ReadUInt16LittleEndian(payload), ReadFlag(payload[2])),
            PingType => new Ping(BinaryPrimitives.ReadInt64LittleEndian(payload)),
            PongType => new Pong(BinaryPrimitives.ReadInt64LittleEndian(payload)),
            StateType => new StateReport(payload[0], payload[1], payload[2]),
            PadStateType => DecodePadState(payload),
            PinRequestType => new PinRequest(payload[0], BinaryPrimitives.ReadUInt16LittleEndian(payload[1..])),
            PenType => DecodePen(payload),
            PinAnswerType => DecodePinAnswer(payload),
            FileEndType => new FileEnd(payload.ToArray()),
            BlobEndType => new BlobEnd(payload.ToArray()),
            FileResultType => new FileResult(payload[0]),
            _ => throw new InvalidDataException($"Unknown frame type 0x{type:x2}"),
        };
    }

    /// <summary>
    /// The longest prefix of <paramref name="value"/> made of whole code points whose UTF-8 fits
    /// <paramref name="maxBytes"/>. One pass by code point, never by UTF-16 unit: cutting a unit at a time
    /// re-measured the whole string on every step, and could stop between the two halves of a surrogate pair,
    /// which then went out as a replacement character. The phone's codec cuts by the same rule, so both
    /// sides send the same bytes for the same text.
    /// </summary>
    public static string Truncate(string value, int maxBytes = MaxTextBytes)
    {
        var end = 0;
        var bytes = 0;
        while (end < value.Length)
        {
            // A lone surrogate decodes as the replacement character, three bytes, which is what the encoder
            // writes for it, so the count stays true for text that was malformed before it got here.
            Rune.DecodeFromUtf16(value.AsSpan(end), out var rune, out var units);
            if (bytes + rune.Utf8SequenceLength > maxBytes)
            {
                break;
            }

            bytes += rune.Utf8SequenceLength;
            end += units;
        }

        return end == value.Length ? value : value[..end];
    }

    /// <summary>
    /// TEXT, FILE_BEGIN, FILE_CHUNK, BLOB_BEGIN and BLOB_CHUNK: an id byte, a length byte, then that many bytes. The
    /// length is checked once here for all of them, so a header that lies closes the link whatever frame it heads.
    /// </summary>
    private static Frame DecodePrefixed(byte type, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < TextHeaderLength || payload.Length != TextHeaderLength + payload[1])
        {
            throw new InvalidDataException($"Frame 0x{type:x2} length does not match its header");
        }

        var body = payload[TextHeaderLength..];
        return type switch
        {
            FileBeginType => DecodeFileBegin(payload[0], body),
            BlobBeginType => DecodeBlobBegin(payload[0], body),
            FileChunkType => new FileChunk(payload[0], body.ToArray()),
            BlobChunkType => new BlobChunk(payload[0], body.ToArray()),
            _ => new Text(payload[0], Encoding.UTF8.GetString(body)),
        };
    }

    /// <summary>The size, then the label, as FILE_BEGIN. Shorter than the size field is malformed; the kind is not checked here.</summary>
    private static BlobBegin DecodeBlobBegin(byte kind, ReadOnlySpan<byte> body)
    {
        if (body.Length < FileSizeBytes)
        {
            throw new InvalidDataException("BLOB_BEGIN is too short to hold a size");
        }

        return new BlobBegin(kind, BinaryPrimitives.ReadUInt32LittleEndian(body), Encoding.UTF8.GetString(body[FileSizeBytes..]));
    }

    /// <summary>The size, then the name. Shorter than the size field is malformed; an empty name is the phone's to refuse.</summary>
    private static FileBegin DecodeFileBegin(byte fileType, ReadOnlySpan<byte> body)
    {
        if (body.Length < FileSizeBytes)
        {
            throw new InvalidDataException("FILE_BEGIN is too short to hold a size");
        }

        return new FileBegin(fileType, BinaryPrimitives.ReadUInt32LittleEndian(body), Encoding.UTF8.GetString(body[FileSizeBytes..]));
    }

    /// <summary>
    /// Nothing here can be out of range, so nothing here throws. Every field spans its whole width — the
    /// triggers are 0..255 and the four axes -32768..32767 — and an undefined button bit is an unknown id,
    /// which this protocol drops and counts rather than treating as an error. That is the line PointerButton
    /// already draws: its id may be anything and is dropped by the dispatcher, while its Down byte throws,
    /// because a flag with two legal values out of 256 means a corrupt stream and not a newer app. A wrong
    /// payload length still closes the connection, as it does for every type.
    /// </summary>
    private static PadState DecodePadState(ReadOnlySpan<byte> payload) => new(
        BinaryPrimitives.ReadUInt16LittleEndian(payload),
        payload[2],
        payload[3],
        ReadInt16(payload[4..]),
        ReadInt16(payload[6..]),
        ReadInt16(payload[8..]),
        ReadInt16(payload[10..]));

    /// <summary>
    /// Nothing here throws: x and y span their whole width, and a state above 2 is not a codec error. A newer
    /// phone may add states, and an unknown one is dropped and counted by the dispatcher like an unknown id,
    /// where closing the link would take the whole session down for it.
    /// </summary>
    private static Pen DecodePen(ReadOnlySpan<byte> payload) => new(
        BinaryPrimitives.ReadUInt16LittleEndian(payload),
        BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]),
        payload[4]);

    /// <summary>
    /// A byte above 9 is not a digit. It closes the link like a bad flag byte, and is not counted as a failed
    /// attempt, since it tests no PIN. The message names no value: a rejected byte is still part of somebody's PIN.
    /// </summary>
    private static PinAnswer DecodePinAnswer(ReadOnlySpan<byte> payload)
    {
        var digits = new char[Pin.Length];
        for (var i = 0; i < digits.Length; i++)
        {
            if (payload[i] > 9)
            {
                throw new InvalidDataException("PIN_ANSWER carries a byte that is not a digit");
            }

            digits[i] = (char)('0' + payload[i]);
        }

        return new PinAnswer(new Pin(new string(digits)));
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
