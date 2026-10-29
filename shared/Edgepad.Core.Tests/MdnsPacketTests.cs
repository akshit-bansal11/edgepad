using System.Net;
using System.Security.Cryptography;
using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

public sealed class MdnsPacketTests
{
    private static readonly MdnsService Service = new(
        "Laptop",
        "edgepad-00112233",
        47321,
        ["id=00112233445566778899aabbccddeeff", "n=Laptop", "v=5", "lv=1"],
        [IPAddress.Parse("192.168.1.5"), IPAddress.Parse("10.0.0.7")]);

    private static readonly byte[] ResponseHeader = [0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 4];

    private static readonly ushort[] InstanceAnswerTypes = [MdnsPacket.TypeSrv, MdnsPacket.TypeA, MdnsPacket.TypeA];

    private static readonly string[] TwoServiceNames = [MdnsPacket.ServiceType, MdnsPacket.ServiceType];

    // A query from a known client: header (1 question), "_edgepad._tcp.local", type PTR, class IN.
    private static readonly byte[] PtrQuery =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x08, .. "_edgepad"u8, 0x04, .. "_tcp"u8, 0x05, .. "local"u8, 0x00,
        0x00, 0x0C, 0x00, 0x01,
    ];

    private static MdnsMessage Parse(byte[] packet)
    {
        Assert.True(MdnsPacket.TryParse(packet, out var message));
        return message!;
    }

    private static byte[] Query(string name, ushort type, ushort cls = 1)
    {
        var bytes = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in name.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(label));
        }

        bytes.AddRange([0, (byte)(type >> 8), (byte)type, (byte)(cls >> 8), (byte)cls]);
        return [.. bytes];
    }

    [Fact]
    public void ABuiltAnnouncementParsesBackFieldForField()
    {
        var message = Parse(MdnsPacket.Build(Service, goodbye: false, MdnsKinds.Ptr, MdnsKinds.Srv | MdnsKinds.Txt | MdnsKinds.A));

        Assert.True(message.IsResponse);
        Assert.Empty(message.Questions);
        Assert.Equal(5, message.Records.Count);

        var ptr = message.Records[0];
        Assert.Equal((MdnsPacket.ServiceType, MdnsPacket.TypePtr, 4500u, "Laptop._edgepad._tcp.local"), (ptr.Name, ptr.Type, ptr.Ttl, ptr.Target));

        var srv = message.Records[1];
        Assert.Equal(("Laptop._edgepad._tcp.local", MdnsPacket.TypeSrv, 120u), (srv.Name, srv.Type, srv.Ttl));
        Assert.Equal(("edgepad-00112233.local", (ushort)47321), (srv.Target, srv.Port));

        var txt = message.Records[2];
        Assert.Equal(MdnsPacket.TypeTxt, txt.Type);
        Assert.Equal(Service.Text, txt.Text);

        Assert.Equal(
            new[] { IPAddress.Parse("192.168.1.5"), IPAddress.Parse("10.0.0.7") },
            message.Records.Skip(3).Select(r => r.Address!));
        Assert.All(message.Records.Skip(3), r => Assert.Equal(("edgepad-00112233.local", MdnsPacket.TypeA, 120u), (r.Name, r.Type, r.Ttl)));
    }

    [Fact]
    public void AGoodbyeIsTheSameRecordsWithATtlOfZero()
    {
        var message = Parse(MdnsPacket.Build(Service, goodbye: true, MdnsKinds.Ptr | MdnsKinds.Srv | MdnsKinds.Txt | MdnsKinds.A, MdnsKinds.None));

        Assert.Equal(5, message.Records.Count);
        Assert.All(message.Records, r => Assert.Equal(0u, r.Ttl));
    }

    [Fact]
    public void AKnownClientsQueryGetsTheExpectedResponseBytes()
    {
        var reply = MdnsPacket.Answer(Service, Parse(PtrQuery));

        Assert.NotNull(reply);
        Assert.False(reply.Unicast);

        // Header: id 0, response + authoritative, 1 answer, 0 authority, 4 additional (SRV, TXT and the two A records).
        Assert.Equal(ResponseHeader, reply.Packet[..12]);

        // The answer: the service name, PTR, class IN (no cache flush), TTL 4500, then the instance name.
        byte[] expected =
        [
            0x08, .. "_edgepad"u8, 0x04, .. "_tcp"u8, 0x05, .. "local"u8, 0x00,
            0x00, 0x0C, 0x00, 0x01, 0x00, 0x00, 0x11, 0x94, 0x00, 0x1C,
            0x06, .. "Laptop"u8, 0x08, .. "_edgepad"u8, 0x04, .. "_tcp"u8, 0x05, .. "local"u8, 0x00,
        ];
        Assert.Equal(expected, reply.Packet[12..(12 + expected.Length)]);
        Assert.Equal(MdnsPacket.Build(Service, goodbye: false, MdnsKinds.Ptr, MdnsKinds.Srv | MdnsKinds.Txt | MdnsKinds.A), reply.Packet);
    }

    [Fact]
    public void AOneShotQueryIsAnsweredWithItsIdAndQuestionAndShortTtls()
    {
        // Sent from port 51234, so the advertiser passes oneShot: the reply is unicast, ID 0x1234, question repeated.
        byte[] query = [.. PtrQuery];
        query[0] = 0x12;
        query[1] = 0x34;

        var reply = MdnsPacket.Answer(Service, Parse(query), oneShot: true);

        Assert.NotNull(reply);
        Assert.True(reply.Unicast);
        byte[] header = [0x12, 0x34, 0x84, 0, 0, 1, 0, 1, 0, 0, 0, 4];
        Assert.Equal(header, reply.Packet[..12]);
        Assert.Equal(PtrQuery[12..], reply.Packet[12..PtrQuery.Length]);
        var message = Parse(reply.Packet);
        Assert.Equal(TwoServiceNames[..1], message.Questions.Select(q => q.Name));
        Assert.All(message.Records, r => Assert.True(r.Ttl <= 10));
        Assert.Equal(5, message.Records.Count);
    }

    [Fact]
    public void AOneShotReplyEchoesOnlyItsOwnQuestionsUnderItsOwnSpelling()
    {
        // A stranger's question, here one whose label no builder may write, is left out rather than copied back.
        var query = new MdnsMessage(
            0x55,
            false,
            [
                new MdnsQuestion(MdnsPacket.ServiceType.ToUpperInvariant(), MdnsPacket.TypePtr, false),
                new MdnsQuestion(new string('x', 70) + ".local", MdnsPacket.TypePtr, false),
            ],
            []);

        var reply = MdnsPacket.Answer(Service, query, oneShot: true);

        Assert.NotNull(reply);
        Assert.Equal([MdnsPacket.ServiceType], Parse(reply.Packet).Questions.Select(q => q.Name));
    }

    [Fact]
    public void TheUnicastBitAsksForAUnicastReply()
    {
        var reply = MdnsPacket.Answer(Service, Parse(Query(MdnsPacket.ServiceType, MdnsPacket.TypePtr, cls: 0x8001)));

        Assert.NotNull(reply);
        Assert.True(reply.Unicast);
    }

    [Fact]
    public void AnInstanceQueryIsAnsweredWithItsRecordsAndTheAddressesAsExtras()
    {
        var reply = MdnsPacket.Answer(Service, Parse(Query("laptop._edgepad._tcp.local", MdnsPacket.TypeSrv)));

        Assert.NotNull(reply);
        var message = Parse(reply.Packet);
        Assert.Equal(InstanceAnswerTypes, message.Records.Select(r => r.Type));
    }

    [Fact]
    public void AHostQueryIsAnsweredWithItsAddresses()
    {
        var reply = MdnsPacket.Answer(Service, Parse(Query("edgepad-00112233.local", MdnsPacket.TypeA)));

        Assert.NotNull(reply);
        Assert.Equal(2, Parse(reply.Packet).Records.Count(r => r.Type == MdnsPacket.TypeA));
    }

    [Theory]
    [InlineData("_edgepad._tcp.local", 28)]
    [InlineData("_edgepad._tcp.local", 16)]
    [InlineData("_other._tcp.local", 12)]
    [InlineData("example.local", 1)]
    [InlineData("laptop._edgepad._tcp.local", 12)]
    public void QuestionsThisServiceHasNoAnswerForAreIgnored(string name, ushort type)
    {
        Assert.Null(MdnsPacket.Answer(Service, Parse(Query(name, type))));
    }

    [Fact]
    public void AResponseFromAnotherHostIsNotAnswered()
    {
        var response = MdnsPacket.Build(Service, goodbye: false, MdnsKinds.Ptr, MdnsKinds.None);

        Assert.Null(MdnsPacket.Answer(Service, Parse(response)));
    }

    [Fact]
    public void AHostQueryWithNoAddressToGiveIsNotAnswered()
    {
        var bare = Service with { Addresses = [] };

        Assert.Null(MdnsPacket.Answer(bare, Parse(Query(bare.HostName, MdnsPacket.TypeA))));
    }

    [Fact]
    public void ACompressionPointerInAQuestionIsFollowed()
    {
        byte[] packet =
        [
            0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x08, .. "_edgepad"u8, 0x04, .. "_tcp"u8, 0x05, .. "local"u8, 0x00, 0x00, 0x0C, 0x00, 0x01,
            0xC0, 0x0C, 0x00, 0x0C, 0x00, 0x01,
        ];

        var message = Parse(packet);

        Assert.Equal(TwoServiceNames, message.Questions.Select(q => q.Name));
    }

    [Fact]
    public void APointerLoopIsRefused()
    {
        byte[] selfPointer =
        [
            0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xC0, 0x0C, 0x00, 0x0C, 0x00, 0x01,
        ];
        byte[] pingPong =
        [
            0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xC0, 0x0E, 0xC0, 0x0C, 0x00, 0x0C, 0x00, 0x01,
        ];

        Assert.False(MdnsPacket.TryParse(selfPointer, out _));
        Assert.False(MdnsPacket.TryParse(pingPong, out _));
    }

    [Fact]
    public void LengthsAndCountsPastWhatThePacketHoldAreRefused()
    {
        var announcement = MdnsPacket.Build(Service, goodbye: false, MdnsKinds.Ptr, MdnsKinds.Srv | MdnsKinds.Txt | MdnsKinds.A);

        // Every prefix is cut off somewhere a field is still owed, except the whole packet.
        for (var length = 0; length < announcement.Length; length++)
        {
            Assert.False(MdnsPacket.TryParse(announcement.AsSpan(0, length), out _), $"A packet cut to {length} bytes was accepted");
        }

        var manyQuestions = (byte[])PtrQuery.Clone();
        manyQuestions[5] = 0xFF;
        var manyRecords = (byte[])PtrQuery.Clone();
        manyRecords[7] = 0xFF;
        var labelPastTheEnd = (byte[])PtrQuery.Clone();
        labelPastTheEnd[12] = 0x3F;
        var reservedLabelBits = (byte[])PtrQuery.Clone();
        reservedLabelBits[12] = 0x48;

        Assert.False(MdnsPacket.TryParse(manyQuestions, out _));
        Assert.False(MdnsPacket.TryParse(manyRecords, out _));
        Assert.False(MdnsPacket.TryParse(labelPastTheEnd, out _));
        Assert.False(MdnsPacket.TryParse(reservedLabelBits, out _));
        Assert.False(MdnsPacket.TryParse(new byte[MdnsPacket.MaxPacket + 1], out _));
    }

    [Fact]
    public void ARecordWhoseDataLengthLiesIsRefused()
    {
        var announcement = MdnsPacket.Build(Service, goodbye: false, MdnsKinds.A, MdnsKinds.None);
        // The first A record: name (23 bytes), type, class, ttl, then its length; claim 200 bytes.
        var at = 12 + 1 + "edgepad-00112233".Length + 1 + "local".Length + 1 + 8;
        announcement[at] = 0;
        announcement[at + 1] = 200;

        Assert.False(MdnsPacket.TryParse(announcement, out _));
    }

    [Fact]
    public void RandomPacketsNeverThrow()
    {
        var packet = new byte[200];
        for (var i = 0; i < 2000; i++)
        {
            RandomNumberGenerator.Fill(packet);
            // A plausible header so the parser gets past the counts and into the names and records.
            packet[4] = 0;
            packet[5] = 1;
            packet[6] = 0;
            packet[7] = 2;
            packet[8] = 0;
            packet[9] = 0;
            packet[10] = 0;
            packet[11] = 0;

            _ = MdnsPacket.TryParse(packet, out _);
        }
    }

    [Fact]
    public void ANameBecomesOneSafeLabel()
    {
        Assert.Equal("my-pc", MdnsPacket.Label("my.pc"));
        Assert.Equal("Edgepad", MdnsPacket.Label("  "));
        Assert.Equal("ab", MdnsPacket.Label("a\u0007b"));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(MdnsPacket.Label(new string('é', 100))) <= 63);
        Assert.Equal("ab", MdnsPacket.Truncate("ab\U0001F600", 5));
    }

    [Fact]
    public void AUnicastAnswerGoesOnlyToAnAskerOnTheLaptopsOwnSubnet()
    {
        (int, IPAddress, IPAddress)[] local = [(3, IPAddress.Parse("192.168.1.20"), IPAddress.Parse("255.255.255.0"))];

        Assert.True(MdnsAdvertiser.OnLink(IPAddress.Parse("192.168.1.77"), local));
        Assert.True(MdnsAdvertiser.OnLink(IPAddress.Parse("192.168.1.77").MapToIPv6(), local));
        Assert.False(MdnsAdvertiser.OnLink(IPAddress.Parse("192.168.2.77"), local));
        Assert.False(MdnsAdvertiser.OnLink(IPAddress.Parse("8.8.8.8"), local));
        Assert.False(MdnsAdvertiser.OnLink(IPAddress.IPv6Loopback, local));
        Assert.False(MdnsAdvertiser.OnLink(IPAddress.Parse("192.168.1.77"), []));

        // A mask too short to mean anything makes nothing on-link, rather than everything.
        (int, IPAddress, IPAddress)[] open = [(3, IPAddress.Parse("10.0.0.5"), IPAddress.Any)];
        Assert.False(MdnsAdvertiser.OnLink(IPAddress.Parse("8.8.8.8"), open));
    }

    [Fact]
    public void ARepeatedQuestionIsEchoedOnce()
    {
        var question = new MdnsQuestion(MdnsPacket.ServiceType, MdnsPacket.TypePtr, false);
        var query = new MdnsMessage(7, false, [.. Enumerable.Repeat(question, 30)], []);

        var reply = MdnsPacket.Answer(Service, query, oneShot: true);

        Assert.NotNull(reply);
        Assert.Single(Parse(reply.Packet).Questions);
    }
}
