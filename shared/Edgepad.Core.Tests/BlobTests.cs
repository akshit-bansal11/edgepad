using System.Buffers.Binary;
using System.Security.Cryptography;
using Edgepad.Blob;
using Edgepad.Net;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The blob lane and the clipboard's rules, on a fake clock and a fake clipboard, so nothing here sleeps or needs a
/// desktop. Everything a phone sends is untrusted: each refusal must leave the receiver able to take the next blob.
/// </summary>
public sealed class BlobTests
{
    private static readonly byte[] A = "copied on the phone"u8.ToArray();
    private static readonly byte[] B = "copied on the laptop"u8.ToArray();

    private readonly List<Frame> wire = [];
    private readonly List<(byte Kind, byte[] Data)> received = [];
    private readonly List<Action> pending = [];
    private readonly FakeClipboard surface = new();
    private readonly BlobReceiver receiver;
    private readonly ClipboardSync sync;
    private long now = 1_000_000;
    private long lastInput;
    private bool toggle = true;

    public BlobTests()
    {
        receiver = new BlobReceiver(TakesClipboardKinds, Keep);
        sync = SyncOver(Sender());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(1024 * 1024)]
    public void ABlobCrossesWhole(int length)
    {
        var data = Payload(length);

        Assert.True(Sender(network: true).Send(BlobKind.ClipText, "clip", data));

        Assert.Equal((length + FrameCodec.MaxChunkBytes - 1) / FrameCodec.MaxChunkBytes, wire.OfType<BlobChunk>().Count());
        Assert.All(wire, frame => Assert.True(Feed(frame)));
        var (kind, got) = Assert.Single(received);
        Assert.Equal((byte)BlobKind.ClipText, kind);
        Assert.Equal(data, got);
    }

    [Fact]
    public void AWrongHashIsRefused()
    {
        var data = Payload(10);

        Assert.True(receiver.Take(new BlobBegin(0, 10, "")));
        Assert.True(receiver.Take(new BlobChunk(0, data)));
        Assert.False(receiver.Take(new BlobEnd(new byte[SHA256.HashSizeInBytes])));

        AssertStillTakesABlob();
    }

    [Fact]
    public void AChunkOutOfOrderDropsTheBlobAndItsTail()
    {
        Assert.True(receiver.Take(new BlobBegin(0, 20, "")));
        Assert.False(receiver.Take(new BlobChunk(1, Payload(10))));
        Assert.True(receiver.Take(new BlobChunk(0, Payload(10))));
        Assert.True(receiver.Take(new BlobEnd(SHA256.HashData(Payload(20)))));

        AssertStillTakesABlob();
    }

    [Fact]
    public void TheSequenceWrapsAt256()
    {
        var data = Payload(300 * FrameCodec.MaxChunkBytes);

        Assert.True(Sender(network: true).Send(BlobKind.ClipText, "", data));

        Assert.All(wire, frame => Assert.True(Feed(frame)));
        Assert.Equal(data, Assert.Single(received).Data);
    }

    [Fact]
    public void FewerBytesThanThePromisedSizeAreRefused()
    {
        var data = Payload(10);

        Assert.True(receiver.Take(new BlobBegin(0, 11, "")));
        Assert.True(receiver.Take(new BlobChunk(0, data)));
        Assert.False(receiver.Take(new BlobEnd(SHA256.HashData(data))));

        AssertStillTakesABlob();
    }

    [Fact]
    public void MoreBytesThanThePromisedSizeAreRefused()
    {
        Assert.True(receiver.Take(new BlobBegin(0, 9, "")));
        Assert.False(receiver.Take(new BlobChunk(0, Payload(10))));
        Assert.True(receiver.Take(new BlobEnd(SHA256.HashData(Payload(10)))));

        AssertStillTakesABlob();
    }

    [Fact]
    public void AnEmptyChunkIsRefused()
    {
        Assert.True(receiver.Take(new BlobBegin(0, 1, "")));
        Assert.False(receiver.Take(new BlobChunk(0, [])));

        AssertStillTakesABlob();
    }

    [Theory]
    [InlineData(ClipboardSync.MaxTextBytes + 1)]
    [InlineData(uint.MaxValue)]
    public void ASizeOverTheCapIsRefusedBeforeAByteIsKept(long size)
    {
        Assert.False(receiver.Take(new BlobBegin(0, (uint)size, "")));
        Assert.True(receiver.Take(new BlobChunk(0, Payload(10))));
        Assert.True(receiver.Take(new BlobEnd(new byte[SHA256.HashSizeInBytes])));

        AssertStillTakesABlob();
    }

    [Fact]
    public void ALabelWithControlCharactersIsRefused()
    {
        Assert.False(receiver.Take(new BlobBegin(0, 1, "a\nb")));

        AssertStillTakesABlob();
    }

    [Fact]
    public void ASecondBeginAbortsTheFirst()
    {
        var data = Payload(10);

        Assert.True(receiver.Take(new BlobBegin(0, 20, "")));
        Assert.True(receiver.Take(new BlobChunk(0, Payload(5))));
        Assert.True(receiver.Take(new BlobBegin(1, 10, "")));
        Assert.True(receiver.Take(new BlobChunk(0, data)));
        Assert.True(receiver.Take(new BlobEnd(SHA256.HashData(data))));

        var (kind, got) = Assert.Single(received);
        Assert.Equal(1, kind);
        Assert.Equal(data, got);
    }

    [Fact]
    public void AnUnknownKindIsCountedOnceAndReadToItsEnd()
    {
        Assert.False(receiver.Take(new BlobBegin(200, 10, "")));
        Assert.True(receiver.Take(new BlobChunk(0, Payload(10))));
        Assert.True(receiver.Take(new BlobEnd(SHA256.HashData(Payload(10)))));

        Assert.Empty(received);
        AssertStillTakesABlob();
    }

    [Fact]
    public void FramesWithNoBlobOpenAreRefused()
    {
        Assert.False(receiver.Take(new BlobChunk(0, Payload(1))));
        Assert.False(receiver.Take(new BlobEnd(SHA256.HashData(Payload(1)))));

        AssertStillTakesABlob();
    }

    [Fact]
    public void AConnectionHasAByteBudget()
    {
        var chunk = new BlobChunk(0, new byte[FrameCodec.MaxChunkBytes]);
        receiver.Take(new BlobBegin(200, 0, ""));
        for (var i = 0; i < BlobReceiver.MaxConnectionBytes / FrameCodec.MaxChunkBytes; i++)
        {
            receiver.Take(chunk);
        }

        var left = (uint)(BlobReceiver.MaxConnectionBytes % FrameCodec.MaxChunkBytes);
        Assert.False(receiver.Take(new BlobBegin(0, left + 1, "")));
        Assert.True(receiver.Take(new BlobBegin(0, left, "")));
    }

    [Theory]
    [InlineData(false, BlobSender.BluetoothChunksPerTick)]
    [InlineData(true, BlobSender.NetworkChunksPerTick)]
    public void AQuietLinkSendsTheBudgetPerTick(bool network, int budget)
    {
        var chunksAtEachTick = new List<int>();
        var sender = Sender(network, ms =>
        {
            Assert.Equal(BlobSender.TickMs, ms);
            chunksAtEachTick.Add(wire.OfType<BlobChunk>().Count());
            return true;
        });

        Assert.True(sender.Send(BlobKind.ClipText, "", Payload(((3 * budget) + 1) * FrameCodec.MaxChunkBytes)));

        int[] expected = [budget, 2 * budget, 3 * budget];
        Assert.Equal(expected, chunksAtEachTick);
    }

    [Fact]
    public void InputDropsTheNextTickToTheBusyBudget()
    {
        var chunksAtEachTick = new List<int>();
        var sender = Sender(pause: _ =>
        {
            chunksAtEachTick.Add(wire.OfType<BlobChunk>().Count());
            lastInput = now;
            return true;
        });

        Assert.True(sender.Send(BlobKind.ClipText, "", Payload(40 * FrameCodec.MaxChunkBytes)));

        Assert.Equal(BlobSender.BluetoothChunksPerTick, chunksAtEachTick[0]);
        Assert.Equal(BlobSender.BluetoothChunksPerTick + BlobSender.BusyChunksPerTick, chunksAtEachTick[1]);
    }

    [Fact]
    public void InputThatHasGoneQuietRestoresTheBudget()
    {
        var chunksAtEachTick = new List<int>();
        lastInput = now;
        var sender = Sender(pause: _ =>
        {
            chunksAtEachTick.Add(wire.OfType<BlobChunk>().Count());
            now += BlobSender.InputQuietMs;
            return true;
        });

        Assert.True(sender.Send(BlobKind.ClipText, "", Payload(40 * FrameCodec.MaxChunkBytes)));

        Assert.Equal(BlobSender.BusyChunksPerTick, chunksAtEachTick[0]);
        Assert.Equal(BlobSender.BusyChunksPerTick + BlobSender.BluetoothChunksPerTick, chunksAtEachTick[1]);
    }

    [Fact]
    public void AnEndedSessionStopsATransferWithNoEnd()
    {
        using var ended = new CancellationTokenSource();
        ended.Cancel();
        var sender = new BlobSender(wire.Add, LinkKind.Bluetooth, () => lastInput, ended.Token, () => now);

        Assert.False(sender.Send(BlobKind.ClipText, "", Payload(100 * FrameCodec.MaxChunkBytes)));

        Assert.Empty(wire.OfType<BlobEnd>());
        Assert.Equal(BlobSender.BluetoothChunksPerTick, wire.OfType<BlobChunk>().Count());
    }

    [Fact]
    public void AnAbandonedTransferStopsWithNoEnd()
    {
        Assert.False(Sender().Send(BlobKind.ClipText, "", Payload(10), () => true));

        Assert.IsType<BlobBegin>(Assert.Single(wire));
    }

    [Fact]
    public void ABlobFromThePhoneIsNotSentBack()
    {
        sync.SetShared(1);

        Remote(A);
        Settle();
        Assert.Equal(A, Assert.Single(surface.Writes).Data);

        now += ClipboardSync.WriteQuietMs;
        sync.LocalChanged();
        Settle();
        Assert.Empty(wire);
    }

    [Fact]
    public void ALocalCopyGoesAndTheOldContentCopiedAgainGoesToo()
    {
        sync.SetShared(1);
        Remote(A);
        Settle();
        now += ClipboardSync.WriteQuietMs;

        surface.Current = new ClipboardContent(BlobKind.ClipText, B);
        sync.LocalChanged();
        Settle();
        Assert.Equal((uint)B.Length, Assert.Single(wire.OfType<BlobBegin>()).Size);

        wire.Clear();
        surface.Current = new ClipboardContent(BlobKind.ClipText, A);
        sync.LocalChanged();
        Settle();
        Assert.Equal((uint)A.Length, Assert.Single(wire.OfType<BlobBegin>()).Size);
    }

    [Fact]
    public void TheSameLocalCopyTwiceGoesOnce()
    {
        sync.SetShared(1);
        surface.Current = new ClipboardContent(BlobKind.ClipText, B);

        sync.LocalChanged();
        Settle();
        sync.LocalChanged();
        Settle();

        Assert.Single(wire.OfType<BlobBegin>());
    }

    [Fact]
    public void AnEchoWithDifferentBytesRightAfterAWriteIsIgnored()
    {
        // A platform can hand an image back re-encoded, so the hash cannot be the only thing that catches it.
        sync.SetShared(1);
        Remote(A);
        Settle();

        surface.Current = new ClipboardContent(BlobKind.ClipText, B);
        sync.LocalChanged();
        Settle();

        Assert.Empty(wire);
    }

    [Fact]
    public void ABurstOfChangesIsOneSend()
    {
        sync.SetShared(1);
        surface.Current = new ClipboardContent(BlobKind.ClipText, B);

        sync.LocalChanged();
        sync.LocalChanged();
        sync.LocalChanged();
        Settle();

        Assert.Single(wire.OfType<BlobBegin>());
    }

    [Fact]
    public void SensitiveContentIsNeverSent()
    {
        sync.SetShared(1);
        surface.Current = new ClipboardContent(BlobKind.ClipText, B, Sensitive: true);

        sync.LocalChanged();
        Settle();

        Assert.Empty(wire);
    }

    [Fact]
    public void NothingMovesUntilThePhoneTurnsItOn()
    {
        surface.Current = new ClipboardContent(BlobKind.ClipText, B);

        sync.LocalChanged();
        Settle();
        Assert.False(sync.Take(new BlobBegin(0, 1, "")));

        Assert.Empty(wire);
        Assert.Empty(surface.Writes);
    }

    [Fact]
    public void TurningItOffStopsBothDirections()
    {
        sync.SetShared(1);
        Assert.True(sync.Take(new BlobBegin(0, (uint)A.Length, "")));
        Assert.True(sync.SetShared(0));
        surface.Current = new ClipboardContent(BlobKind.ClipText, B);

        Assert.False(sync.Take(new BlobChunk(0, A)));
        Assert.False(sync.Take(new BlobEnd(SHA256.HashData(A))));
        sync.LocalChanged();
        Settle();

        Assert.Empty(wire);
        Assert.Empty(surface.Writes);
    }

    [Fact]
    public void TheOwnersToggleOutranksThePhone()
    {
        toggle = false;
        Assert.False(sync.SetShared(1));

        toggle = true;
        Assert.True(sync.SetShared(1));
        toggle = false;
        surface.Current = new ClipboardContent(BlobKind.ClipText, B);
        sync.LocalChanged();
        Settle();

        Assert.Empty(wire);
        Assert.False(sync.Take(new BlobBegin(0, 1, "")));
    }

    [Fact]
    public void AValueThatIsNeitherOnNorOffIsRefused()
    {
        Assert.False(sync.SetShared(2));

        sync.LocalChanged();
        Assert.Empty(pending);
    }

    [Fact]
    public void TurningItOnSendsNothingUntilTheNextCopy()
    {
        surface.Current = new ClipboardContent(BlobKind.ClipText, B);

        sync.SetShared(1);
        Settle();

        Assert.Empty(wire);
    }

    [Fact]
    public void ACopyOverTheCapStaysHere()
    {
        sync.SetShared(1);
        surface.Current = new ClipboardContent(BlobKind.ClipText, new byte[ClipboardSync.MaxTextBytes + 1]);

        sync.LocalChanged();
        Settle();

        Assert.Empty(wire);
    }

    [Fact]
    public void TextThatIsNotUtf8IsDropped()
    {
        byte[] invalid = [0xC3, 0x28];
        sync.SetShared(1);

        Assert.False(RemoteLastFrame(BlobKind.ClipText, invalid));

        Assert.Empty(surface.Writes);
    }

    [Fact]
    public void AnEmptyBlobIsDropped()
    {
        sync.SetShared(1);

        Assert.False(RemoteLastFrame(BlobKind.ClipText, []));

        Assert.Empty(surface.Writes);
    }

    [Fact]
    public void AKindTheClipboardDoesNotTakeIsDropped()
    {
        sync.SetShared(1);

        Assert.False(sync.Take(new BlobBegin((byte)BlobKind.AppList, 1, "")));
    }

    [Fact]
    public void APngIsWritten()
    {
        var png = Png(7680, 4320);
        sync.SetShared(1);

        Assert.True(RemoteLastFrame(BlobKind.ClipImage, png));
        Settle();

        var written = Assert.Single(surface.Writes);
        Assert.Equal(BlobKind.ClipImage, written.Kind);
        Assert.Equal(png, written.Data);
    }

    [Theory]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    [InlineData(100_000, 100_000)]
    [InlineData(0, 100)]
    public void APngThatWouldDecodeToTooMuchIsDropped(uint width, uint height)
    {
        sync.SetShared(1);

        Assert.False(RemoteLastFrame(BlobKind.ClipImage, Png(width, height)));

        Assert.Empty(surface.Writes);
    }

    [Fact]
    public void AnImageThatIsNotAPngIsDropped()
    {
        sync.SetShared(1);

        Assert.False(RemoteLastFrame(BlobKind.ClipImage, Payload(100)));

        Assert.Empty(surface.Writes);
    }

    [Fact]
    public void AClipboardThatThrowsEndsNothing()
    {
        sync.SetShared(1);
        surface.Throws = true;

        Remote(A);
        Settle();
        now += ClipboardSync.WriteQuietMs;
        sync.LocalChanged();
        Settle();

        Assert.Empty(wire);
    }

    [Fact]
    public void ASendThatFailedIsTriedAgainOnTheNextCopy()
    {
        var fail = true;
        var failing = SyncOver(new BlobSender(
            frame =>
            {
                if (fail)
                {
                    throw new IOException("the link went");
                }

                wire.Add(frame);
            },
            LinkKind.Bluetooth,
            () => lastInput,
            TestContext.Current.CancellationToken,
            () => now,
            _ => true));
        failing.SetShared(1);
        surface.Current = new ClipboardContent(BlobKind.ClipText, B);

        failing.LocalChanged();
        Settle();
        fail = false;
        failing.LocalChanged();
        Settle();

        Assert.Single(wire.OfType<BlobEnd>());
    }

    [Fact]
    public void TurningItOffPartWayStopsTheTransfer()
    {
        ClipboardSync? stopping = null;
        var sender = Sender(pause: _ =>
        {
            stopping?.SetShared(0);
            return true;
        });
        stopping = SyncOver(sender);
        stopping.SetShared(1);
        surface.Current = new ClipboardContent(BlobKind.ClipText, Payload(100 * FrameCodec.MaxChunkBytes));

        stopping.LocalChanged();
        Settle();

        Assert.Equal(BlobSender.BluetoothChunksPerTick, wire.OfType<BlobChunk>().Count());
        Assert.Empty(wire.OfType<BlobEnd>());
    }

    [Fact]
    public void TheSameContentGoesAgainOnceSharingWasTurnedOffAndOn()
    {
        // The phone may have been on another laptop since, so what this one remembers sending proves nothing.
        sync.SetShared(1);
        surface.Current = new ClipboardContent(BlobKind.ClipText, B);
        sync.LocalChanged();
        Settle();

        sync.SetShared(0);
        sync.SetShared(1);
        sync.LocalChanged();
        Settle();

        Assert.Equal(2, wire.OfType<BlobBegin>().Count());
    }

    private static byte[] Payload(int length) => [.. Enumerable.Range(0, length).Select(i => (byte)(i * 31))];

    private static long? TakesClipboardKinds(byte kind) => kind <= (byte)BlobKind.ClipImage ? ClipboardSync.MaxTextBytes : null;

    /// <summary>A PNG as far as its header: the signature, IHDR's length and name, then the two sizes.</summary>
    private static byte[] Png(uint width, uint height)
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), height);
        return png;
    }

    private BlobSender Sender(bool network = false, Func<int, bool>? pause = null) => new(
        wire.Add,
        network ? LinkKind.Network : LinkKind.Bluetooth,
        () => lastInput,
        TestContext.Current.CancellationToken,
        () => now,
        pause ?? (_ => true));

    private ClipboardSync SyncOver(BlobSender sender) => new(
        surface,
        sender,
        () => toggle,
        TestContext.Current.CancellationToken,
        () => now,
        (_, work) => pending.Add(work));

    private bool Keep(byte kind, byte[] data, byte[] sha256)
    {
        Assert.Equal(SHA256.HashData(data), sha256);
        received.Add((kind, data));
        return true;
    }

    private bool Feed(Frame frame) => frame switch
    {
        BlobBegin b => receiver.Take(b),
        BlobChunk c => receiver.Take(c),
        BlobEnd e => receiver.Take(e),
        _ => false,
    };

    private void AssertStillTakesABlob()
    {
        Assert.Empty(received);
        var data = Payload(300);

        Assert.True(Sender().Send(BlobKind.ClipImage, "", data));

        Assert.All(wire, frame => Assert.True(Feed(frame)));
        Assert.Equal(data, Assert.Single(received).Data);
    }

    /// <summary>Time for a debounce to pass, then everything the sync put off, as its scheduler would run it.</summary>
    private void Settle()
    {
        now += ClipboardSync.DebounceMs;
        Action[] due = [.. pending];
        pending.Clear();
        foreach (var work in due)
        {
            work();
        }
    }

    private void Remote(byte[] data) => Assert.True(RemoteLastFrame(BlobKind.ClipText, data));

    /// <summary>Plays a blob into the sync as the phone would, and answers what it said to the BLOB_END.</summary>
    private bool RemoteLastFrame(BlobKind kind, byte[] data)
    {
        var inbound = new List<Frame>();
        new BlobSender(inbound.Add, LinkKind.Network, () => 0, TestContext.Current.CancellationToken, () => now, _ => true)
            .Send(kind, "", data);

        var answer = false;
        foreach (var frame in inbound)
        {
            answer = frame switch
            {
                BlobBegin b => sync.Take(b),
                BlobChunk c => sync.Take(c),
                BlobEnd e => sync.Take(e),
                _ => false,
            };
        }

        return answer;
    }
}

/// <summary>A clipboard in memory, which notes what it was asked to write.</summary>
internal sealed class FakeClipboard : IClipboardSurface
{
    public ClipboardContent? Current { get; set; }

    public List<ClipboardContent> Writes { get; } = [];

    public bool Throws { get; set; }

    public ClipboardContent? Read() => Throws ? throw new IOException("the clipboard is locked") : Current;

    public void Write(ClipboardContent content)
    {
        if (Throws)
        {
            throw new IOException("the clipboard is locked");
        }

        Writes.Add(content);
        Current = content;
    }
}
