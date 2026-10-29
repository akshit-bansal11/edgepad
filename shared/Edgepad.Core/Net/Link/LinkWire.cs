using System.Buffers.Binary;
using System.Text;

namespace Edgepad.Net;

/// <summary>
/// The cleartext half of the network link, EDGL1: the bytes a phone and a laptop exchange before anything is
/// encrypted, and only their shape. Every message is <c>u16 length | body</c>, little-endian, at most
/// <see cref="MaxMessage"/> bytes of body, so a peer that never authenticates can never make this side allocate
/// more than that. What the values mean, and in what order they are sent, is <see cref="LinkResponder"/>'s.
///
/// Requests (phone to laptop) open with "EDGL", the link version and a mode. Replies (laptop to phone) open with
/// a status byte; any status but <see cref="StatusOk"/> is the whole reply, and the laptop closes after it.
/// </summary>
internal static class LinkWire
{
    public const byte LinkVersion = 1;

    public const byte ModeInfo = 0;
    public const byte ModePair = 1;
    public const byte ModeResume = 2;

    public const byte StatusOk = 0;

    /// <summary>Another pairing is under way, or pairing is locked after failed attempts.</summary>
    public const byte StatusBusy = 1;

    /// <summary>The phone's link version is not this laptop's.</summary>
    public const byte StatusUnsupported = 2;

    /// <summary>A resume from a phone this laptop holds no pairing key for: the phone should pair again.</summary>
    public const byte StatusUnknownPhone = 3;

    public const int MaxMessage = 512;
    public const int MaxRecordPlain = 16384;

    public const int IdLength = 16;
    public const int NonceLength = 16;
    public const int PublicKeyLength = 65;
    public const int HashLength = 32;
    public const int MaxNameBytes = 255;

    public static ReadOnlySpan<byte> Magic => "EDGL"u8;

    /// <summary>Sends one message and flushes it.</summary>
    public static void Write(Stream output, ReadOnlySpan<byte> body)
    {
        if (body.Length > MaxMessage)
        {
            throw new ArgumentException($"A link message is at most {MaxMessage} bytes", nameof(body));
        }

        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(length, (ushort)body.Length);
        output.Write(length);
        output.Write(body);
        output.Flush();
    }

    /// <summary>The next message's body. A closed stream throws <see cref="EndOfStreamException"/>, as a read does.</summary>
    public static byte[] Read(Stream input)
    {
        Span<byte> length = stackalloc byte[2];
        input.ReadExactly(length);
        var n = BinaryPrimitives.ReadUInt16LittleEndian(length);
        if (n > MaxMessage)
        {
            throw new InvalidDataException($"A link message of {n} bytes, over the {MaxMessage} allowed");
        }

        var body = new byte[n];
        input.ReadExactly(body);
        return body;
    }

    public static byte[] EncodeInfoRequest() => Request(ModeInfo).ToArray();

    public static byte[] EncodePairRequest(ReadOnlySpan<byte> phoneId, string name, ReadOnlySpan<byte> commit) =>
        Request(ModePair).Bytes(phoneId, IdLength).Name(name).Bytes(commit, HashLength).ToArray();

    public static byte[] EncodeResumeRequest(ReadOnlySpan<byte> phoneId, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> nonce) =>
        Request(ModeResume).Bytes(phoneId, IdLength).Bytes(publicKey, PublicKeyLength).Bytes(nonce, NonceLength).ToArray();

    /// <summary>
    /// A phone's opening message. A link version this laptop does not speak decodes as
    /// <see cref="UnsupportedRequest"/> without reading further, since its layout is not this one's to know.
    /// </summary>
    public static LinkRequest DecodeRequest(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        if (!r.Bytes(Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not an Edgepad link request");
        }

        var version = r.Byte();
        if (version != LinkVersion)
        {
            return new UnsupportedRequest(version);
        }

        LinkRequest request = r.Byte() switch
        {
            ModeInfo => new InfoRequest(),
            ModePair => new PairRequest(r.Bytes(IdLength).ToArray(), r.Name(), r.Bytes(HashLength).ToArray()),
            ModeResume => new ResumeRequest(r.Bytes(IdLength).ToArray(), r.Bytes(PublicKeyLength).ToArray(), r.Bytes(NonceLength).ToArray()),
            var mode => throw new InvalidDataException($"Unknown link mode {mode}"),
        };
        r.End();
        return request;
    }

    public static byte[] Status(byte status) => [status];

    public static byte[] InfoReply(ReadOnlySpan<byte> laptopId, byte protocolVersion, string name) =>
        new Builder().Byte(StatusOk).Bytes(laptopId, IdLength).Byte(protocolVersion).Name(name).ToArray();

    public static byte[] PairOffer(ReadOnlySpan<byte> laptopId, string name, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> nonce) =>
        new Builder().Byte(StatusOk).Bytes(laptopId, IdLength).Name(name).Bytes(publicKey, PublicKeyLength).Bytes(nonce, NonceLength).ToArray();

    /// <summary>The phone's key and nonce, sent only once the laptop's are on the wire: they open its commitment.</summary>
    public static byte[] Reveal(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> nonce) =>
        new Builder().Bytes(publicKey, PublicKeyLength).Bytes(nonce, NonceLength).ToArray();

    public static byte[] ResumeReply(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> tag) =>
        new Builder().Byte(StatusOk).Bytes(publicKey, PublicKeyLength).Bytes(nonce, NonceLength).Bytes(tag, HashLength).ToArray();

    /// <summary>A key-confirmation tag on its own, as both sides send one.</summary>
    public static byte[] Tag(ReadOnlySpan<byte> tag) => new Builder().Bytes(tag, HashLength).ToArray();

    public static (byte[] PublicKey, byte[] Nonce) DecodeReveal(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        var reveal = (r.Bytes(PublicKeyLength).ToArray(), r.Bytes(NonceLength).ToArray());
        r.End();
        return reveal;
    }

    public static byte[] DecodeTag(ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        var tag = r.Bytes(HashLength).ToArray();
        r.End();
        return tag;
    }

    /// <summary>
    /// A laptop's reply, as the phone reads it: <see cref="LinkReply.Status"/> alone for a refusal, otherwise the
    /// fields of the reply to <paramref name="mode"/>.
    /// </summary>
    public static LinkReply DecodeReply(byte mode, ReadOnlySpan<byte> body)
    {
        var r = new Reader(body);
        var status = r.Byte();
        if (status != StatusOk)
        {
            r.End();
            return new LinkReply(status, [], 0, "", [], [], []);
        }

        LinkReply reply = mode switch
        {
            ModeInfo => new LinkReply(status, r.Bytes(IdLength).ToArray(), r.Byte(), r.Name(), [], [], []),
            ModePair => new LinkReply(status, r.Bytes(IdLength).ToArray(), 0, r.Name(), r.Bytes(PublicKeyLength).ToArray(), r.Bytes(NonceLength).ToArray(), []),
            ModeResume => new LinkReply(status, [], 0, "", r.Bytes(PublicKeyLength).ToArray(), r.Bytes(NonceLength).ToArray(), r.Bytes(HashLength).ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        r.End();
        return reply;
    }

    private static Builder Request(byte mode) => new Builder().Bytes(Magic, Magic.Length).Byte(LinkVersion).Byte(mode);

    private sealed class Builder
    {
        private readonly List<byte> bytes = [];

        public Builder Byte(byte value)
        {
            bytes.Add(value);
            return this;
        }

        public Builder Bytes(ReadOnlySpan<byte> value, int length)
        {
            if (value.Length != length)
            {
                throw new ArgumentException($"Expected {length} bytes, got {value.Length}", nameof(value));
            }

            bytes.AddRange(value);
            return this;
        }

        /// <summary>A display name, UTF-8 behind a one-byte length, cut at a character boundary to fit.</summary>
        public Builder Name(string name)
        {
            var utf8 = Encoding.UTF8.GetBytes(name);
            var length = Math.Min(utf8.Length, MaxNameBytes);
            while (length < utf8.Length && length > 0 && (utf8[length] & 0xC0) == 0x80)
            {
                length--;
            }

            bytes.Add((byte)length);
            bytes.AddRange(utf8.AsSpan(0, length));
            return this;
        }

        public byte[] ToArray() => [.. bytes];
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> body;
        private int position;

        public Reader(ReadOnlySpan<byte> body) => this.body = body;

        public ReadOnlySpan<byte> Bytes(int length)
        {
            if (body.Length - position < length)
            {
                throw new InvalidDataException("A link message ended early");
            }

            var value = body.Slice(position, length);
            position += length;
            return value;
        }

        public byte Byte() => Bytes(1)[0];

        public string Name() => Encoding.UTF8.GetString(Bytes(Byte()));

        public readonly void End()
        {
            if (position != body.Length)
            {
                throw new InvalidDataException("A link message ran on past its fields");
            }
        }
    }
}

internal abstract record LinkRequest;

internal sealed record InfoRequest : LinkRequest;

internal sealed record UnsupportedRequest(byte Version) : LinkRequest;

internal sealed record PairRequest(byte[] PhoneId, string Name, byte[] Commit) : LinkRequest;

internal sealed record ResumeRequest(byte[] PhoneId, byte[] PublicKey, byte[] Nonce) : LinkRequest;

/// <summary>A laptop's reply as a phone decodes it; fields a mode does not carry are empty.</summary>
internal sealed record LinkReply(byte Status, byte[] LaptopId, byte ProtocolVersion, string Name, byte[] PublicKey, byte[] Nonce, byte[] Tag);
