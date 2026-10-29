using System.Net;
using System.Text;

namespace Edgepad.Net;

/// <summary>Which records of a service a packet carries.</summary>
[Flags]
internal enum MdnsKinds
{
    None = 0,
    Ptr = 1,
    Srv = 2,
    Txt = 4,
    A = 8,
}

/// <summary>What the laptop announces: <c>Instance._edgepad._tcp.local</c> on <c>Host.local</c>, one A record per address.</summary>
internal sealed record MdnsService(string Instance, string Host, ushort Port, IReadOnlyList<string> Text, IReadOnlyList<IPAddress> Addresses)
{
    public string InstanceName => $"{Instance}.{MdnsPacket.ServiceType}";

    public string HostName => $"{Host}.local";
}

internal sealed record MdnsQuestion(string Name, ushort Type, bool Unicast);

/// <summary>One record read from a packet. Only the fields of its own type are set; other types are not kept.</summary>
internal sealed record MdnsRecord(string Name, ushort Type, uint Ttl, string Target, ushort Port, IReadOnlyList<string> Text, IPAddress? Address);

internal sealed record MdnsMessage(bool IsResponse, IReadOnlyList<MdnsQuestion> Questions, IReadOnlyList<MdnsRecord> Records);

/// <summary>An answer to a query, and whether the asker wanted it by unicast (the QU bit).</summary>
internal sealed record MdnsReply(byte[] Packet, bool Unicast);

/// <summary>
/// The mDNS wire format (RFC 6762) for the one service this laptop announces, with no sockets: a builder that never
/// compresses names, and a parser that reads packets from anyone on the network. Every length, count and offset is
/// checked against the packet and a cap before it is used, a compression pointer is followed at most
/// <see cref="MaxPointerHops"/> times, and a packet that is not exactly well formed is refused whole, never partly read.
/// </summary>
internal static class MdnsPacket
{
    public const string ServiceType = "_edgepad._tcp.local";

    /// <summary>The largest packet read; a bigger datagram is not one a phone sends.</summary>
    public const int MaxPacket = 9000;

    public const ushort TypeA = 1;
    public const ushort TypePtr = 12;
    public const ushort TypeTxt = 16;
    public const ushort TypeSrv = 33;
    public const ushort TypeAny = 255;

    private const int HeaderLength = 12;
    private const int MaxQuestions = 32;
    private const int MaxRecords = 128;
    private const int MaxPointerHops = 16;
    private const int MaxNameLength = 255;
    private const int MaxLabelLength = 63;
    private const uint LongTtl = 4500;
    private const uint ShortTtl = 120;

    /// <summary>
    /// The response to <paramref name="query"/>, or null when it asks for nothing this service has: a response, an
    /// unknown name or type, or the host's address when there is none to give.
    /// </summary>
    public static MdnsReply? Answer(MdnsService service, MdnsMessage query)
    {
        if (query.IsResponse)
        {
            return null;
        }

        var answers = MdnsKinds.None;
        var unicast = false;
        foreach (var q in query.Questions)
        {
            var asked = Asked(service, q);
            answers |= asked;
            unicast |= asked != MdnsKinds.None && q.Unicast;
        }

        if (answers == MdnsKinds.None || (answers == MdnsKinds.A && service.Addresses.Count == 0))
        {
            return null;
        }

        var additional = MdnsKinds.None;
        if (answers.HasFlag(MdnsKinds.Ptr))
        {
            additional = MdnsKinds.Srv | MdnsKinds.Txt | MdnsKinds.A;
        }
        else if (answers.HasFlag(MdnsKinds.Srv))
        {
            additional = MdnsKinds.A;
        }

        return new MdnsReply(Build(service, goodbye: false, answers, additional & ~answers), unicast);
    }

    public static byte[] Build(MdnsService service, bool goodbye, MdnsKinds answers, MdnsKinds additional)
    {
        var packet = new List<byte>(512);
        U16(packet, 0);
        U16(packet, 0x8400);
        U16(packet, 0);
        U16(packet, Count(service, answers));
        U16(packet, 0);
        U16(packet, Count(service, additional));
        Write(packet, service, goodbye, answers);
        Write(packet, service, goodbye, additional);
        return [.. packet];
    }

    /// <summary>A name that is one DNS label: no dots, no control characters, at most 63 UTF-8 bytes, never empty.</summary>
    public static string Label(string name)
    {
        var clean = Truncate(string.Concat(name.Where(c => !char.IsControl(c)).Select(c => c == '.' ? '-' : c)).Trim(), MaxLabelLength);
        return clean.Length == 0 ? "Edgepad" : clean;
    }

    public static string Truncate(string text, int maxBytes)
    {
        while (Encoding.UTF8.GetByteCount(text) > maxBytes)
        {
            text = text[..^1];
        }

        return text.Length > 0 && char.IsHighSurrogate(text[^1]) ? text[..^1] : text;
    }

    /// <summary>False for anything that is not a well-formed packet within the caps; never throws.</summary>
    public static bool TryParse(ReadOnlySpan<byte> packet, out MdnsMessage? message)
    {
        message = null;
        if (packet.Length is < HeaderLength or > MaxPacket)
        {
            return false;
        }

        var questions = U16(packet, 4);
        var records = U16(packet, 6) + U16(packet, 8) + U16(packet, 10);
        if (questions > MaxQuestions || records > MaxRecords)
        {
            return false;
        }

        var position = HeaderLength;
        var parsedQuestions = new List<MdnsQuestion>(questions);
        for (var i = 0; i < questions; i++)
        {
            if (!TryName(packet, ref position, out var name) || position + 4 > packet.Length)
            {
                return false;
            }

            parsedQuestions.Add(new MdnsQuestion(name, (ushort)U16(packet, position), (U16(packet, position + 2) & 0x8000) != 0));
            position += 4;
        }

        var parsedRecords = new List<MdnsRecord>();
        for (var i = 0; i < records; i++)
        {
            if (!TryRecord(packet, ref position, out var record))
            {
                return false;
            }

            if (record is not null)
            {
                parsedRecords.Add(record);
            }
        }

        message = new MdnsMessage((U16(packet, 2) & 0x8000) != 0, parsedQuestions, parsedRecords);
        return true;
    }

    private static MdnsKinds Asked(MdnsService service, MdnsQuestion q)
    {
        var any = q.Type == TypeAny;
        if (Same(q.Name, ServiceType))
        {
            return any || q.Type == TypePtr ? MdnsKinds.Ptr : MdnsKinds.None;
        }

        if (Same(q.Name, service.InstanceName))
        {
            return (any || q.Type == TypeSrv ? MdnsKinds.Srv : MdnsKinds.None) | (any || q.Type == TypeTxt ? MdnsKinds.Txt : MdnsKinds.None);
        }

        return Same(q.Name, service.HostName) && (any || q.Type == TypeA) ? MdnsKinds.A : MdnsKinds.None;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static int Count(MdnsService service, MdnsKinds kinds) =>
        (kinds.HasFlag(MdnsKinds.Ptr) ? 1 : 0) + (kinds.HasFlag(MdnsKinds.Srv) ? 1 : 0) + (kinds.HasFlag(MdnsKinds.Txt) ? 1 : 0)
        + (kinds.HasFlag(MdnsKinds.A) ? service.Addresses.Count : 0);

    private static void Write(List<byte> packet, MdnsService service, bool goodbye, MdnsKinds kinds)
    {
        // A goodbye is the same records with a TTL of 0. The cache-flush bit goes on every record that is the only one
        // of its name and type, which is all but the shared PTR.
        uint Ttl(uint normal) => goodbye ? 0 : normal;
        if (kinds.HasFlag(MdnsKinds.Ptr))
        {
            Record(packet, ServiceType, TypePtr, flush: false, Ttl(LongTtl), data => Name(data, service.InstanceName));
        }

        if (kinds.HasFlag(MdnsKinds.Srv))
        {
            Record(packet, service.InstanceName, TypeSrv, flush: true, Ttl(ShortTtl), data =>
            {
                U16(data, 0);
                U16(data, 0);
                U16(data, service.Port);
                Name(data, service.HostName);
            });
        }

        if (kinds.HasFlag(MdnsKinds.Txt))
        {
            Record(packet, service.InstanceName, TypeTxt, flush: true, Ttl(LongTtl), data =>
            {
                foreach (var entry in service.Text)
                {
                    var bytes = Encoding.UTF8.GetBytes(entry);
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(bytes.Length, 255);
                    data.Add((byte)bytes.Length);
                    data.AddRange(bytes);
                }

                if (service.Text.Count == 0)
                {
                    data.Add(0);
                }
            });
        }

        if (kinds.HasFlag(MdnsKinds.A))
        {
            foreach (var address in service.Addresses)
            {
                Record(packet, service.HostName, TypeA, flush: true, Ttl(ShortTtl), data => data.AddRange(address.GetAddressBytes()));
            }
        }
    }

    private static void Record(List<byte> packet, string name, int type, bool flush, uint ttl, Action<List<byte>> data)
    {
        Name(packet, name);
        U16(packet, type);
        U16(packet, flush ? 0x8001 : 1);
        U32(packet, ttl);
        var lengthAt = packet.Count;
        U16(packet, 0);
        data(packet);
        var length = packet.Count - lengthAt - 2;
        packet[lengthAt] = (byte)(length >> 8);
        packet[lengthAt + 1] = (byte)length;
    }

    private static void Name(List<byte> packet, string name)
    {
        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            ArgumentOutOfRangeException.ThrowIfZero(bytes.Length);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(bytes.Length, MaxLabelLength);
            packet.Add((byte)bytes.Length);
            packet.AddRange(bytes);
        }

        packet.Add(0);
    }

    private static void U16(List<byte> packet, int value)
    {
        packet.Add((byte)(value >> 8));
        packet.Add((byte)value);
    }

    private static void U32(List<byte> packet, uint value)
    {
        U16(packet, (int)(value >> 16));
        U16(packet, (int)(value & 0xFFFF));
    }

    private static int U16(ReadOnlySpan<byte> packet, int at) => (packet[at] << 8) | packet[at + 1];

    private static uint U32(ReadOnlySpan<byte> packet, int at) => ((uint)U16(packet, at) << 16) | (uint)U16(packet, at + 2);

    /// <summary>
    /// Reads the name at <paramref name="position"/> and leaves it just past its in-place bytes. A pointer is followed
    /// at most <see cref="MaxPointerHops"/> times and the labels read may total no more than 255 bytes, so a pointer
    /// loop or a long chain ends in a refusal rather than a hang or an allocation.
    /// </summary>
    private static bool TryName(ReadOnlySpan<byte> packet, ref int position, out string name)
    {
        name = "";
        var labels = new StringBuilder();
        var at = position;
        var end = -1;
        var hops = 0;
        var total = 0;
        while (true)
        {
            if (at >= packet.Length)
            {
                return false;
            }

            int b = packet[at];
            if (b == 0)
            {
                end = end < 0 ? at + 1 : end;
                break;
            }

            if ((b & 0xC0) == 0xC0)
            {
                if (at + 1 >= packet.Length || ++hops > MaxPointerHops)
                {
                    return false;
                }

                end = end < 0 ? at + 2 : end;
                at = ((b & 0x3F) << 8) | packet[at + 1];
                continue;
            }

            if ((b & 0xC0) != 0 || at + 1 + b > packet.Length)
            {
                return false;
            }

            total += b + 1;
            if (total > MaxNameLength)
            {
                return false;
            }

            if (labels.Length > 0)
            {
                labels.Append('.');
            }

            labels.Append(Encoding.UTF8.GetString(packet.Slice(at + 1, b)));
            at += 1 + b;
        }

        position = end;
        name = labels.ToString();
        return true;
    }

    private static bool TryRecord(ReadOnlySpan<byte> packet, ref int position, out MdnsRecord? record)
    {
        record = null;
        if (!TryName(packet, ref position, out var name) || position + 10 > packet.Length)
        {
            return false;
        }

        var type = (ushort)U16(packet, position);
        var ttl = U32(packet, position + 4);
        var length = U16(packet, position + 8);
        position += 10;
        if (length > packet.Length - position)
        {
            return false;
        }

        var start = position;
        var end = position + length;
        position = end;
        var at = start;
        switch (type)
        {
            case TypePtr:
                if (!TryName(packet, ref at, out var target) || at > end)
                {
                    return false;
                }

                record = new MdnsRecord(name, type, ttl, target, 0, [], null);
                break;
            case TypeSrv:
                at = start + 6;
                if (length < 7 || !TryName(packet, ref at, out var host) || at > end)
                {
                    return false;
                }

                record = new MdnsRecord(name, type, ttl, host, (ushort)U16(packet, start + 4), [], null);
                break;
            case TypeTxt:
                var text = new List<string>();
                while (at < end)
                {
                    var n = packet[at];
                    if (at + 1 + n > end)
                    {
                        return false;
                    }

                    text.Add(Encoding.UTF8.GetString(packet.Slice(at + 1, n)));
                    at += 1 + n;
                }

                record = new MdnsRecord(name, type, ttl, "", 0, text, null);
                break;
            case TypeA:
                if (length != 4)
                {
                    return false;
                }

                record = new MdnsRecord(name, type, ttl, "", 0, [], new IPAddress(packet.Slice(start, 4)));
                break;
            default:
                break;
        }

        return true;
    }
}
