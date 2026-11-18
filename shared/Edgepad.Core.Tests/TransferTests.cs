using System.Security.Cryptography;
using Edgepad.Net;
using Edgepad.Protocol;
using Edgepad.Transfer;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The transfer engine with its two ends wired straight together in memory: a frame the sender writes is handed to the
/// receiver on the spot and the receiver's reply goes straight back, so no socket, thread or timer is involved. A hook
/// sits on the wire to cut it, drop a frame or damage one.
/// </summary>
public sealed class TransferTests : IDisposable
{
    private const long Plenty = long.MaxValue / 2;

    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource ended = new();
    private readonly List<XferStatus> replies = [];
    private readonly List<string> arrived = [];
    private readonly List<(string Name, ulong Held, ulong Size)> progressed = [];
    private readonly List<(string Name, XferCode Code)> failed = [];
    private readonly TransferSender sender;
    private DateTimeOffset clockAt = DateTimeOffset.UtcNow;
    private TransferReceiver receiver;

    // Sees each frame on its way to the receiver and says what is delivered: the same frame, another, or null for none.
    private Func<Frame, Frame?> wire = frame => frame;

    public TransferTests()
    {
        sender = new TransferSender(Deliver, LinkKind.Network, () => 0, ended.Token, TimeSpan.FromMilliseconds(300), pause: _ => true);
        receiver = NewReceiver();
    }

    private string Destination => Path.Combine(dir, "Downloads", "Edgepad");

    private string Partial => Path.Combine(dir, "partial");

    public void Dispose()
    {
        receiver.Dispose();
        ended.Dispose();
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AFileArrivesWholeAndTheReceiverReportsProgress()
    {
        var data = RandomNumberGenerator.GetBytes(300 * 1024);
        var path = Source("photo.bin", data);

        Assert.Equal(XferCode.Saved, Send(path));

        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "photo.bin")));
        Assert.Equal([Path.Combine(Destination, "photo.bin")], arrived);
        Assert.Empty(Directory.GetFiles(Partial));
        Assert.Equal(new XferStatus(1, (byte)XferCode.Ready, 0), replies[0]);
        Assert.Contains(replies, r => r.Status == (byte)XferCode.Progress);
        Assert.Equal(new XferStatus(1, (byte)XferCode.Saved, (ulong)data.Length), replies[^1]);
    }

    [Fact]
    public void AnEmptyFileArrives()
    {
        Assert.Equal(XferCode.Saved, Send(Source("empty.txt", [])));
        Assert.Empty(File.ReadAllBytes(Path.Combine(Destination, "empty.txt")));
    }

    [Fact]
    public void ACutAtAChunkBoundaryResumesWhereTheReceiverStopped()
    {
        var data = RandomNumberGenerator.GetBytes(2000);
        var path = Source("cut.bin", data);
        var chunks = 0;
        wire = frame => frame is XferChunk && ++chunks == 4 ? throw new IOException("cut") : frame;

        Assert.Throws<IOException>(() => Send(path));
        receiver.Dispose();
        var held = 3 * FrameCodec.MaxXferChunkBytes;
        Assert.Equal(held, new FileInfo(Path.Combine(Partial, "cut.bin.2000.part")).Length);
        Assert.False(File.Exists(Path.Combine(Destination, "cut.bin")));

        wire = frame => frame;
        replies.Clear();
        receiver = NewReceiver();
        Assert.Equal(XferCode.Saved, Send(path));

        Assert.Equal(new XferStatus(1, (byte)XferCode.Ready, (ulong)held), replies[0]);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "cut.bin")));
    }

    [Fact]
    public void APartHeldMidChunkResumesFromItsLength()
    {
        var data = RandomNumberGenerator.GetBytes(1000);
        var path = Source("mid.bin", data);
        Directory.CreateDirectory(Partial);
        File.WriteAllBytes(Path.Combine(Partial, "mid.bin.1000.part"), data[..100]);

        Assert.Equal(XferCode.Saved, Send(path));

        Assert.Equal(new XferStatus(1, (byte)XferCode.Ready, 100), replies[0]);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "mid.bin")));
    }

    [Fact]
    public void APartLongerThanTheFileIsStartedOver()
    {
        var data = RandomNumberGenerator.GetBytes(500);
        var path = Source("long.bin", data);
        Directory.CreateDirectory(Partial);
        File.WriteAllBytes(Path.Combine(Partial, "long.bin.500.part"), new byte[900]);

        Assert.Equal(XferCode.Saved, Send(path));
        Assert.Equal(new XferStatus(1, (byte)XferCode.Ready, 0), replies[0]);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "long.bin")));
    }

    [Fact]
    public void AWrongHashSavesNothingAndDeletesThePart()
    {
        var path = Source("bad.bin", RandomNumberGenerator.GetBytes(1000));
        var first = true;
        wire = frame =>
        {
            if (frame is XferChunk chunk && first)
            {
                first = false;
                var damaged = chunk.Data.ToArray();
                damaged[0] ^= 0xFF;
                return chunk with { Data = damaged };
            }

            return frame;
        };

        Assert.Equal(XferCode.BadHash, Send(path));

        Assert.False(Directory.Exists(Destination) && Directory.GetFiles(Destination).Length > 0);
        Assert.Empty(Directory.GetFiles(Partial));
        Assert.Empty(arrived);
    }

    [Fact]
    public void AGapInOffsetsIsAnsweredWithTheBytesHeld()
    {
        receiver.Take(new XferBegin(1, 1000, "gap.bin"));
        receiver.Take(new XferChunk(1, 0, new byte[100]));
        replies.Clear();

        Assert.True(receiver.Take(new XferChunk(1, 150, new byte[10])));
        Assert.True(receiver.Take(new XferChunk(1, 50, new byte[10])));

        Assert.Equal([new XferStatus(1, (byte)XferCode.Progress, 100), new XferStatus(1, (byte)XferCode.Progress, 100)], replies);
        Assert.True(receiver.Busy);
    }

    [Fact]
    public void DataPastTheSizeRejectsAndDropsThePart()
    {
        receiver.Take(new XferBegin(1, 100, "over.bin"));
        replies.Clear();

        Assert.True(receiver.Take(new XferChunk(1, 0, new byte[101])));

        Assert.Equal([new XferStatus(1, (byte)XferCode.Rejected, 0)], replies);
        Assert.False(receiver.Busy);
        Assert.Empty(Directory.GetFiles(Partial));
    }

    [Fact]
    public void ASecondBeginWhileOneIsActiveIsBusyAndLeavesTheFirstAlone()
    {
        receiver.Take(new XferBegin(1, 100, "one.bin"));
        replies.Clear();

        Assert.True(receiver.Take(new XferBegin(2, 100, "two.bin")));

        Assert.Equal([new XferStatus(2, (byte)XferCode.Busy, 0)], replies);
        Assert.True(receiver.Take(new XferChunk(1, 0, new byte[10])));
        Assert.False(receiver.Take(new XferChunk(2, 0, new byte[10])));
    }

    [Fact]
    public void TheReceiverCancellingStopsTheSender()
    {
        var path = Source("rc.bin", RandomNumberGenerator.GetBytes(3000));
        var chunks = 0;
        wire = frame =>
        {
            if (frame is XferChunk && ++chunks == 3)
            {
                Assert.True(receiver.Cancel());
            }

            return frame;
        };

        Assert.Equal(XferCode.Cancelled, Send(path));
        Assert.False(receiver.Busy);
        Assert.Empty(Directory.GetFiles(Partial));
    }

    [Fact]
    public void TheSenderCancellingStopsTheReceiver()
    {
        var path = Source("sc.bin", RandomNumberGenerator.GetBytes(3000));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var chunks = 0;
        wire = frame =>
        {
            if (frame is XferChunk && ++chunks == 3)
            {
                cancel.Cancel();
            }

            return frame;
        };

        Assert.Equal(XferCode.Cancelled, sender.Send(path, 1, cancel.Token));

        Assert.False(receiver.Busy);
        Assert.Empty(Directory.GetFiles(Partial));
        Assert.False(File.Exists(Path.Combine(Destination, "sc.bin")));
    }

    [Fact]
    public void AFileOverTwoGiBIsRejected()
    {
        Assert.True(receiver.Take(new XferBegin(1, 3UL << 30, "big.bin")));
        Assert.Equal([new XferStatus(1, (byte)XferCode.Rejected, 0)], replies);
        Assert.False(receiver.Busy);
    }

    [Fact]
    public void TooLittleFreeSpaceRejectsAndTheMarginCounts()
    {
        receiver.Dispose();
        receiver = NewReceiver(_ => 10 + TransferLimits.FreeSpaceMargin - 1);
        receiver.Take(new XferBegin(1, 10, "full.bin"));
        Assert.Equal([new XferStatus(1, (byte)XferCode.Rejected, 0)], replies);

        replies.Clear();
        receiver = NewReceiver(_ => 10 + TransferLimits.FreeSpaceMargin);
        receiver.Take(new XferBegin(1, 10, "full.bin"));
        Assert.Equal([new XferStatus(1, (byte)XferCode.Ready, 0)], replies);
    }

    [Fact]
    public void FreeSpaceThatCannotBeReadRefusesTheFileAndTheSessionGoesOn()
    {
        receiver.Dispose();
        receiver = NewReceiver(_ => throw new ArgumentException("UNC"));
        Assert.True(receiver.Take(new XferBegin(1, 10, "share.bin")));
        Assert.Equal([new XferStatus(1, (byte)XferCode.Rejected, 0)], replies);
        Assert.False(receiver.Busy);

        replies.Clear();
        receiver = NewReceiver(_ => -1);
        receiver.Take(new XferBegin(2, 0, "unknown.bin"));
        Assert.Equal([new XferStatus(2, (byte)XferCode.Rejected, 0)], replies);
    }

    [Fact]
    public void TheVolumeThatHoldsTheFolderIsTheOneMeasured()
    {
        var s = Path.DirectorySeparatorChar;
        var root = Path.GetPathRoot(Path.GetFullPath(dir))!;
        var home = root + "home";
        var mounts = new List<(string Root, Func<long> Free)> { (root, () => 1), (home, () => 2) };
        Assert.Equal(2, TransferReceiver.FreeSpaceFor(home + s + "u" + s + "Downloads", mounts));
        Assert.Equal(2, TransferReceiver.FreeSpaceFor(home, mounts));
        Assert.Equal(1, TransferReceiver.FreeSpaceFor(home + "work", mounts));
        Assert.Equal(1, TransferReceiver.FreeSpaceFor(root + "srv", mounts));
        Assert.Equal(-1, TransferReceiver.FreeSpaceFor(root + "srv", []));
    }

    [Fact]
    public void AStaleMountThatThrowsWhenReadDoesNotRefuseAFolderOnAnother()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(dir))!;
        var stale = root + "stale";
        var mounts = new List<(string Root, Func<long> Free)>
        {
            (stale, () => throw new IOException("stale FUSE mount")),
            (root, () => 7),
        };

        Assert.Equal(7, TransferReceiver.FreeSpaceFor(root + "srv", mounts));
    }

    [Theory]
    [InlineData("CON.txt")]
    [InlineData("..")]
    [InlineData("")]
    public void AnUnusableNameIsRejected(string name)
    {
        Assert.True(receiver.Take(new XferBegin(1, 10, name)));
        Assert.Equal([new XferStatus(1, (byte)XferCode.Rejected, 0)], replies);
        Assert.False(receiver.Busy);
    }

    [Fact]
    public void ASecondFileOfTheSameNameIsNumberedNeverOverwritten()
    {
        var first = RandomNumberGenerator.GetBytes(400);
        var second = RandomNumberGenerator.GetBytes(450);

        Assert.Equal(XferCode.Saved, Send(Source("same.bin", first)));
        Assert.Equal(XferCode.Saved, Send(Source("same.bin", second)));

        Assert.Equal(first, File.ReadAllBytes(Path.Combine(Destination, "same.bin")));
        Assert.Equal(second, File.ReadAllBytes(Path.Combine(Destination, "same (1).bin")));
    }

    [Fact]
    public void PartsOlderThanSevenDaysAreDeletedWhenTheReceiverIsMade()
    {
        Directory.CreateDirectory(Partial);
        var old = Path.Combine(Partial, "old.5.part");
        var recent = Path.Combine(Partial, "recent.5.part");
        File.WriteAllBytes(old, new byte[5]);
        File.WriteAllBytes(recent, new byte[5]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow.AddDays(-6));

        receiver.Dispose();
        receiver = NewReceiver();

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void NoAnswerIsATimeoutAndAnEndedSessionStopsTheSend()
    {
        var path = Source("quiet.bin", RandomNumberGenerator.GetBytes(100));
        wire = _ => null;
        Assert.Equal(XferCode.TimedOut, Send(path));

        ended.Cancel();
        Assert.Equal(XferCode.SessionEnded, Send(path));
    }

    [Fact]
    public void TheFileIsMarkedWhileStillAPartBeforeItEntersTheDestination()
    {
        receiver.Dispose();
        var seen = new List<(string Path, bool InPartial, bool InDestination)>();
        receiver = new TransferReceiver(
            Destination,
            Partial,
            status =>
            {
                replies.Add(status);
                sender.Take(status);
            },
            arrived.Add,
            _ => Plenty,
            () => clockAt,
            part => seen.Add((part, File.Exists(part), File.Exists(Path.Combine(Destination, "mark.bin")))));

        Assert.Equal(XferCode.Saved, Send(Source("mark.bin", [1, 2, 3])));

        Assert.Equal([(Path.Combine(Partial, "mark.bin.3.part"), true, false)], seen);
        Assert.True(File.Exists(Path.Combine(Destination, "mark.bin")));
    }

    [Fact]
    public void ALostChunkIsResentFromWhatTheReceiverHolds()
    {
        var data = RandomNumberGenerator.GetBytes(8 * FrameCodec.MaxXferChunkBytes);
        var path = Source("lost.bin", data);
        var chunks = 0;
        wire = frame => frame is XferChunk && ++chunks == 3 ? null : frame;

        Assert.Equal(XferCode.Saved, Send(path));

        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "lost.bin")));
    }

    [Fact]
    public void ALostLastChunkIsResentAfterEnd()
    {
        var data = RandomNumberGenerator.GetBytes(3 * FrameCodec.MaxXferChunkBytes);
        var path = Source("tail.bin", data);
        var chunks = 0;
        wire = frame => frame is XferChunk && ++chunks == 3 ? null : frame;

        Assert.Equal(XferCode.Saved, Send(path));

        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "tail.bin")));
    }

    [Fact]
    public void ATimeoutTellsTheReceiverCancelled()
    {
        var sent = new List<Frame>();
        wire = frame =>
        {
            sent.Add(frame);
            return null;
        };

        Assert.Equal(XferCode.TimedOut, Send(Source("quiet2.bin", new byte[10])));

        Assert.Contains(sent, f => f is XferStatus { Status: (byte)XferCode.Cancelled });
    }

    [Fact]
    public void ATransferWithNoChunkFor30SecondsIsDroppedAndItsPartKept()
    {
        receiver.Take(new XferBegin(1, 100, "idle.bin"));
        receiver.Take(new XferChunk(1, 0, new byte[10]));
        replies.Clear();

        clockAt += TransferLimits.IdleDrop - TimeSpan.FromSeconds(1);
        receiver.Take(new XferBegin(2, 100, "next.bin"));
        Assert.Equal([new XferStatus(2, (byte)XferCode.Busy, 0)], replies);

        replies.Clear();
        clockAt += TimeSpan.FromSeconds(2);
        Assert.False(receiver.Busy);
        Assert.Equal([new XferStatus(1, (byte)XferCode.Cancelled, 0)], replies);
        replies.Clear();
        receiver.Take(new XferBegin(2, 100, "next.bin"));
        Assert.Equal([new XferStatus(2, (byte)XferCode.Ready, 0)], replies);
        Assert.True(File.Exists(Path.Combine(Partial, "idle.bin.100.part")));
    }

    [Fact]
    public void AChunkLostMidTransferIsResentBeforeEndIsSent()
    {
        var path = Source("lost.bin", RandomNumberGenerator.GetBytes(FrameCodec.MaxXferChunkBytes * 6));
        var lostAt = (ulong)FrameCodec.MaxXferChunkBytes;
        var seen = new List<Frame>();
        var dropped = false;
        wire = frame =>
        {
            seen.Add(frame);
            if (!dropped && frame is XferChunk { Offset: var at } && at == lostAt)
            {
                dropped = true;
                return null;
            }

            return frame;
        };

        Assert.Equal(XferCode.Saved, Send(path));

        var firstEnd = seen.FindIndex(f => f is XferEnd);
        Assert.Equal(2, seen.Take(firstEnd).OfType<XferChunk>().Count(c => c.Offset == lostAt));
    }

    [Fact]
    public void StaleProgressAfterARewindDoesNotRewindAgain()
    {
        var path = Source("stale.bin", RandomNumberGenerator.GetBytes(FrameCodec.MaxXferChunkBytes * 6));
        var lostAt = (ulong)FrameCodec.MaxXferChunkBytes;
        var stale = new XferStatus(1, (byte)XferCode.Progress, lostAt);
        var atLostOffset = 0;
        wire = frame =>
        {
            if (frame is XferChunk { Offset: var at } && at == lostAt)
            {
                atLostOffset++;
                if (atLostOffset == 1)
                {
                    return null;
                }

                if (atLostOffset == 2)
                {
                    Assert.True(sender.Take(stale));
                    Assert.True(sender.Take(stale));
                }
            }

            return frame;
        };

        Assert.Equal(XferCode.Saved, Send(path));

        Assert.Equal(2, atLostOffset);
    }

    [Fact]
    public void AStatusItDoesNotKnowOrAnotherIdIsNotTaken()
    {
        Assert.False(sender.Take(new XferStatus(1, (byte)XferCode.Ready, 0)));
        Assert.False(receiver.Take(new XferStatus(1, (byte)XferCode.Cancelled, 0)));
        Assert.False(receiver.Take(new XferChunk(9, 0, new byte[1])));
        Assert.False(receiver.Take(new XferEnd(9, new byte[SHA256.HashSizeInBytes])));
    }

    [Fact]
    public void TheSenderReportsRisingOffsetsEndingAtTheSize()
    {
        var size = 1024 * 1024;
        var path = Source("prog.bin", RandomNumberGenerator.GetBytes(size));
        var seen = new List<ulong>();

        Assert.Equal(XferCode.Saved, sender.Send(path, 1, TestContext.Current.CancellationToken, seen.Add));

        Assert.NotEmpty(seen);
        Assert.Equal(seen.Order(), seen);
        Assert.Equal((ulong)size, seen[^1]);
    }

    [Fact]
    public void TheReceiverReportsProgressAndNoFailureOnACleanSave()
    {
        var size = 300 * 1024;

        Assert.Equal(XferCode.Saved, Send(Source("p.bin", RandomNumberGenerator.GetBytes(size))));

        Assert.Equal(progressed.Select(h => h.Held).Order(), progressed.Select(h => h.Held));
        Assert.Equal(((ulong)size, (ulong)size), (progressed[^1].Held, progressed[^1].Size));
        Assert.All(progressed, h => Assert.Equal("p.bin", h.Name));
        Assert.Empty(failed);
    }

    [Fact]
    public void AWrongHashIsReportedOnceAsBadHash()
    {
        var first = true;
        wire = frame =>
        {
            if (frame is XferChunk chunk && first)
            {
                first = false;
                var damaged = chunk.Data.ToArray();
                damaged[0] ^= 0xFF;
                return chunk with { Data = damaged };
            }

            return frame;
        };

        Assert.Equal(XferCode.BadHash, Send(Source("bh.bin", RandomNumberGenerator.GetBytes(1000))));

        Assert.Equal([("bh.bin", XferCode.BadHash)], failed);
    }

    [Fact]
    public void ARefusedBeginIsReportedAsRejected()
    {
        receiver.Take(new XferBegin(1, 3UL << 30, "big.bin"));

        Assert.Equal([("big.bin", XferCode.Rejected)], failed);
    }

    [Fact]
    public void CancelIsReportedAsCancelled()
    {
        receiver.Take(new XferBegin(1, 100, "c.bin"));

        Assert.True(receiver.Cancel());

        Assert.Equal([("c.bin", XferCode.Cancelled)], failed);
    }

    private TransferReceiver NewReceiver(Func<string, long>? freeSpace = null) =>
        new(
            Destination,
            Partial,
            status =>
            {
                replies.Add(status);
                sender.Take(status);
            },
            arrived.Add,
            freeSpace ?? (_ => Plenty),
            () => clockAt,
            progress: (name, held, size) => progressed.Add((name, held, size)),
            failed: (name, code) => failed.Add((name, code)));

    private XferCode Send(string path) => sender.Send(path, 1, TestContext.Current.CancellationToken);

    private string Source(string name, byte[] data)
    {
        var folder = Path.Combine(dir, "source", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    private void Deliver(Frame sent)
    {
        switch (wire(sent))
        {
            case XferBegin f:
                receiver.Take(f);
                break;
            case XferChunk f:
                receiver.Take(f);
                break;
            case XferEnd f:
                receiver.Take(f);
                break;
            case XferStatus f:
                receiver.Take(f);
                break;
            default:
                break;
        }
    }
}
