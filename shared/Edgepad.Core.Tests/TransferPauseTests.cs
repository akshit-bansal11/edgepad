using System.Collections.Concurrent;
using System.Security.Cryptography;
using Edgepad.Net;
using Edgepad.Protocol;
using Edgepad.Transfer;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// Pause and resume of the transfer engine (K2): the two ends wired straight together in memory, the sender running on its
/// own task so it can sit held while the test works, and both clocks fake so a ten minute hold costs nothing. A hook on the
/// wire lets a test pause or drop at an exact chunk.
/// </summary>
public sealed class TransferPauseTests : IDisposable
{
    private const long Plenty = long.MaxValue / 2;
    private const int Chunk = FrameCodec.MaxXferChunkBytes;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-pause-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource ended = new();
    private readonly ConcurrentQueue<Frame> sentFrames = new();
    private readonly ConcurrentQueue<XferStatus> replies = new();
    private readonly ConcurrentQueue<(string Name, XferCode Code)> failed = new();
    private readonly ConcurrentQueue<bool> senderHeld = new();
    private readonly ConcurrentQueue<bool> receiverHeld = new();
    private readonly TransferSender sender;
    private readonly TransferReceiver receiver;
    private long tickMs = 1_000_000;
    private long clockTicks = DateTimeOffset.UtcNow.UtcTicks;
    private int chunks;

    // Sees each frame on its way to the receiver and says what is delivered: the same frame, another, or null for none.
    private Func<Frame, Frame?> wire = frame => frame;

    public TransferPauseTests()
    {
        sender = new TransferSender(
            Deliver,
            LinkKind.Network,
            () => 0,
            ended.Token,
            TimeSpan.FromSeconds(30),
            () => Interlocked.Read(ref tickMs),
            pause: _ => true,
            pausedChanged: senderHeld.Enqueue);
        receiver = new TransferReceiver(
            Destination,
            Partial,
            status =>
            {
                replies.Enqueue(status);
                sender.Take(status);
            },
            freeSpace: _ => Plenty,
            now: () => new DateTimeOffset(Interlocked.Read(ref clockTicks), TimeSpan.Zero),
            failed: (name, code) => failed.Enqueue((name, code)),
            pausedChanged: receiverHeld.Enqueue,
            holdWatch: TimeSpan.FromMilliseconds(20));
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
    public async Task ASenderPauseHoldsTheFileAndResumeFinishesItWithTheSameHash()
    {
        var data = RandomNumberGenerator.GetBytes(Chunk * 6);
        wire = frame =>
        {
            if (frame is XferChunk && Interlocked.Increment(ref chunks) == 3)
            {
                Assert.True(sender.Pause());
            }

            return frame;
        };

        var send = StartSend(Source("s.bin", data));
        await Until(() => SentStatuses().Any(s => s.Status == (byte)XferCode.Paused));

        Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));
        Assert.Equal(3, Volatile.Read(ref chunks));
        Assert.Contains(new XferStatus(1, (byte)XferCode.Paused, (ulong)(3 * Chunk)), SentStatuses());
        Assert.Equal<bool>([true], receiverHeld);
        Assert.Equal<bool>([true], senderHeld);

        Assert.True(sender.Resume());
        Assert.Equal(XferCode.Saved, await send.WaitAsync(Patience, TestContext.Current.CancellationToken));

        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "s.bin")));
        Assert.Contains(new XferStatus(1, (byte)XferCode.Resume, (ulong)(3 * Chunk)), SentStatuses());
        Assert.Contains(new XferStatus(1, (byte)XferCode.Ready, (ulong)(3 * Chunk)), replies);
        var offsets = sentFrames.OfType<XferChunk>().Select(c => c.Offset).ToArray();
        Assert.Equal(offsets.Order(), offsets);
        Assert.Equal(6, offsets.Length);
        Assert.Equal<bool>([true, false], receiverHeld);
        Assert.Equal<bool>([true, false], senderHeld);
    }

    [Fact]
    public async Task AReceiverPauseHoldsTheFileAndResumeRewindsTheSenderToTheBytesHeld()
    {
        var data = RandomNumberGenerator.GetBytes(Chunk * 6);
        var dropped = false;
        wire = frame =>
        {
            // The chunk in flight when the receiver pauses is lost, so the receiver holds one chunk less than was sent.
            if (frame is XferChunk && !dropped && Interlocked.Increment(ref chunks) == 3)
            {
                dropped = true;
                Assert.True(receiver.Pause());
                return null;
            }

            return frame;
        };

        var send = StartSend(Source("r.bin", data));
        await Until(() => senderHeld.Contains(true));

        Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));
        Assert.Equal(3, Volatile.Read(ref chunks));
        Assert.Contains(new XferStatus(1, (byte)XferCode.Paused, (ulong)(2 * Chunk)), replies);

        Assert.True(receiver.Resume());
        Assert.Equal(XferCode.Saved, await send.WaitAsync(Patience, TestContext.Current.CancellationToken));

        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "r.bin")));
        Assert.Contains(new XferStatus(1, (byte)XferCode.Ready, (ulong)(2 * Chunk)), replies);
        Assert.Equal<bool>([true, false], receiverHeld);
        Assert.Equal<bool>([true, false], senderHeld);
    }

    [Fact]
    public void ARemoteResumeIsAnsweredWithReadyAtTheHeldOffsetAndNotBeforeTheOwnHoldEnds()
    {
        receiver.Take(new XferBegin(1, 100, "q.bin"));
        receiver.Take(new XferChunk(1, 0, new byte[10]));
        replies.Clear();

        Assert.True(receiver.Take(new XferStatus(1, (byte)XferCode.Paused, 10)));
        Assert.Empty(replies);
        Assert.True(receiver.Take(new XferStatus(1, (byte)XferCode.Resume, 10)));
        Assert.Equal<XferStatus>([new XferStatus(1, (byte)XferCode.Ready, 10)], replies);

        // Both hold: this end's RESUME alone is no READY while the sender still holds, and the sender's RESUME is answered
        // only once this end has let go.
        replies.Clear();
        Assert.True(receiver.Pause());
        Assert.True(receiver.Take(new XferStatus(1, (byte)XferCode.Paused, 10)));
        replies.Clear();
        Assert.True(receiver.Resume());
        Assert.Equal<XferStatus>([new XferStatus(1, (byte)XferCode.Resume, 10)], replies);
        replies.Clear();
        Assert.True(receiver.Take(new XferStatus(1, (byte)XferCode.Resume, 10)));
        Assert.Equal<XferStatus>([new XferStatus(1, (byte)XferCode.Ready, 10)], replies);

        replies.Clear();
        Assert.True(receiver.Pause());
        Assert.True(receiver.Take(new XferStatus(1, (byte)XferCode.Paused, 10)));
        replies.Clear();
        Assert.True(receiver.Take(new XferStatus(1, (byte)XferCode.Resume, 10)));
        Assert.Empty(replies);
        Assert.True(receiver.Resume());
        Assert.Equal<XferStatus>([new XferStatus(1, (byte)XferCode.Resume, 10), new XferStatus(1, (byte)XferCode.Ready, 10)], replies);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenBothEndsPauseEachLetsGoOnItsOwnAndNeitherStaysStuck(bool senderResumesFirst)
    {
        var data = RandomNumberGenerator.GetBytes(Chunk * 6);
        wire = frame =>
        {
            if (frame is XferChunk && Interlocked.Increment(ref chunks) == 3)
            {
                Assert.True(sender.Pause());
                Assert.True(receiver.Pause());
            }

            return frame;
        };

        var send = StartSend(Source("b.bin", data));
        await Until(() => SentStatuses().Any(s => s.Status == (byte)XferCode.Paused));
        Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));

        if (senderResumesFirst)
        {
            Assert.True(sender.Resume());
            await Until(() => SentStatuses().Any(s => s.Status == (byte)XferCode.Resume));
            Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));
            Assert.True(receiver.Resume());
        }
        else
        {
            Assert.True(receiver.Resume());
            Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));
            Assert.True(sender.Resume());
        }

        Assert.Equal(XferCode.Saved, await send.WaitAsync(Patience, TestContext.Current.CancellationToken));
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "b.bin")));
        Assert.Equal<bool>([true, false], receiverHeld);
        Assert.Equal<bool>([true, false], senderHeld);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NoAnswerTimeoutAndNoIdleDropRunWhilePaused(bool bySender)
    {
        var data = RandomNumberGenerator.GetBytes(Chunk * 6);
        wire = frame =>
        {
            if (frame is XferChunk && Interlocked.Increment(ref chunks) == 3)
            {
                Assert.True(bySender ? sender.Pause() : receiver.Pause());
            }

            return frame;
        };

        var send = StartSend(Source("t.bin", data));
        await Until(() => senderHeld.Contains(true) && receiverHeld.Contains(true));
        Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));

        // Far past the 30 second answer timeout and idle drop, short of the cap.
        Advance(TimeSpan.FromMinutes(9));

        Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(300)));
        Assert.True(receiver.Busy);
        Assert.Empty(failed);
        Assert.DoesNotContain(sentFrames.OfType<XferStatus>(), s => s.Status == (byte)XferCode.Cancelled);

        Assert.True(bySender ? sender.Resume() : receiver.Resume());
        Assert.Equal(XferCode.Saved, await send.WaitAsync(Patience, TestContext.Current.CancellationToken));
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "t.bin")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task APauseOlderThanTenMinutesEndsCancelledAndKeepsThePartForTheNextSend(bool bySender)
    {
        var data = RandomNumberGenerator.GetBytes(Chunk * 6);
        var pausing = true;
        wire = frame =>
        {
            if (pausing && frame is XferChunk && Interlocked.Increment(ref chunks) == 3)
            {
                Assert.True(bySender ? sender.Pause() : receiver.Pause());
            }

            return frame;
        };
        var path = Source("cap.bin", data);

        var send = StartSend(path);
        await Until(() => senderHeld.Contains(true) && receiverHeld.Contains(true));
        Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));
        Assert.Equal(3, Volatile.Read(ref chunks));

        Advance(TransferLimits.PauseCap + TimeSpan.FromSeconds(1));

        Assert.Equal(XferCode.Cancelled, await send.WaitAsync(Patience, TestContext.Current.CancellationToken));
        Assert.False(receiver.Busy);
        Assert.Equal<(string, XferCode)>([("cap.bin", XferCode.Cancelled)], failed);
        Assert.Equal([Path.Combine(Partial, $"cap.bin.{data.Length}.part")], Directory.GetFiles(Partial));
        Assert.Equal<bool>([true, false], receiverHeld);
        Assert.Equal<bool>([true, false], senderHeld);
        Assert.Empty(Directory.GetFiles(Destination));

        // The part kept is what the next send starts from, and the file still arrives whole.
        pausing = false;
        var again = await StartSend(path).WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(XferCode.Saved, again);
        Assert.Contains(new XferStatus(1, (byte)XferCode.Ready, (ulong)(3 * Chunk)), replies);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "cap.bin")));
    }

    [Fact]
    public async Task TheSenderCancellingWhilePausedStopsBothAndDeletesThePart()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        wire = frame =>
        {
            if (frame is XferChunk && Interlocked.Increment(ref chunks) == 3)
            {
                Assert.True(sender.Pause());
            }

            return frame;
        };

        var send = StartSend(Source("cs.bin", RandomNumberGenerator.GetBytes(Chunk * 6)), cancel);
        await Until(() => SentStatuses().Any(s => s.Status == (byte)XferCode.Paused));
        Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));

        await cancel.CancelAsync();

        Assert.Equal(XferCode.Cancelled, await send.WaitAsync(Patience, TestContext.Current.CancellationToken));
        Assert.False(receiver.Busy);
        Assert.Equal<(string, XferCode)>([("cs.bin", XferCode.Cancelled)], failed);
        Assert.Empty(Directory.GetFiles(Partial));
        Assert.Equal<bool>([true, false], receiverHeld);
    }

    [Fact]
    public async Task TheReceiverCancellingWhilePausedStopsBothAndDeletesThePart()
    {
        wire = frame =>
        {
            if (frame is XferChunk && Interlocked.Increment(ref chunks) == 3)
            {
                Assert.True(receiver.Pause());
            }

            return frame;
        };

        var send = StartSend(Source("cr.bin", RandomNumberGenerator.GetBytes(Chunk * 6)));
        await Until(() => senderHeld.Contains(true));
        Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));

        Assert.True(receiver.Cancel());

        Assert.Equal(XferCode.Cancelled, await send.WaitAsync(Patience, TestContext.Current.CancellationToken));
        Assert.False(receiver.Busy);
        Assert.Empty(Directory.GetFiles(Partial));
        Assert.Equal<bool>([true, false], receiverHeld);
        Assert.Equal<bool>([true, false], senderHeld);
    }

    [Fact]
    public async Task AStatusForAnotherIdChangesNothingAndANeedlessResumeIsHarmless()
    {
        var data = RandomNumberGenerator.GetBytes(Chunk * 4);
        wire = frame =>
        {
            if (frame is XferChunk && Interlocked.Increment(ref chunks) == 2)
            {
                Assert.False(sender.Take(new XferStatus(9, (byte)XferCode.Paused, 0)));
                Assert.False(sender.Take(new XferStatus(9, (byte)XferCode.Resume, 0)));
                Assert.True(sender.Take(new XferStatus(1, (byte)XferCode.Resume, 0)));
                Assert.False(receiver.Take(new XferStatus(9, (byte)XferCode.Paused, 0)));
            }

            return frame;
        };

        Assert.Equal(XferCode.Saved, await StartSend(Source("w.bin", data)).WaitAsync(Patience, TestContext.Current.CancellationToken));

        Assert.Empty(senderHeld);
        Assert.Empty(receiverHeld);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "w.bin")));
    }

    [Fact]
    public void PauseAndResumeDoNothingWithoutAFileOrAHold()
    {
        Assert.False(sender.Pause());
        Assert.False(sender.Resume());
        Assert.False(receiver.Pause());
        Assert.False(receiver.Resume());

        receiver.Take(new XferBegin(1, 100, "n.bin"));

        Assert.False(receiver.Resume());
        Assert.True(receiver.Pause());
        Assert.False(receiver.Pause());
        Assert.True(receiver.Resume());
        Assert.False(receiver.Resume());
    }

    [Fact]
    public async Task TheLocalOnlyCodesAreOutOfTheWireRangeAndNeverReachTheWire()
    {
        Assert.Equal(0xF0, (byte)XferCode.TimedOut);
        Assert.Equal(0xF1, (byte)XferCode.SessionEnded);
        Assert.Equal(8, (byte)XferCode.Paused);
        Assert.Equal(9, (byte)XferCode.Resume);

        // A frame that carries one is an unknown status: dropped, taken by neither end.
        receiver.Take(new XferBegin(1, 100, "z.bin"));
        Assert.False(receiver.Take(new XferStatus(1, (byte)XferCode.TimedOut, 0)));
        Assert.False(receiver.Take(new XferStatus(1, (byte)XferCode.SessionEnded, 0)));
        Assert.False(receiver.Take(new XferStatus(1, 10, 0)));
        receiver.Cancel();

        // No answer: the sender times out, and says CANCELLED to the peer, not TIMED_OUT.
        wire = _ => null;
        var send = StartSend(Source("quiet.bin", RandomNumberGenerator.GetBytes(10)));
        await Until(() => sentFrames.OfType<XferBegin>().Any());
        Assert.False(sender.Take(new XferStatus(1, (byte)XferCode.TimedOut, 0)));

        // Long enough that the sender is waiting, with its deadline set, before the clock moves.
        Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));
        Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(XferCode.TimedOut, await send.WaitAsync(Patience, TestContext.Current.CancellationToken));

        // A receiver idle drop answers CANCELLED, and a receiver that is disposed mid-file answers nothing.
        wire = frame => frame;
        receiver.Take(new XferBegin(2, 100, "idle.bin"));
        receiver.Take(new XferChunk(2, 0, new byte[10]));
        Advance(TransferLimits.IdleDrop + TimeSpan.FromSeconds(1));
        Assert.False(receiver.Busy);
        receiver.Take(new XferBegin(3, 100, "open.bin"));
        receiver.Dispose();

        // A session that ended under a send.
        await ended.CancelAsync();
        Assert.Equal(XferCode.SessionEnded, sender.Send(Source("gone.bin", new byte[10]), 1, cancel: TestContext.Current.CancellationToken));

        Assert.Contains(("idle.bin", XferCode.TimedOut), failed);
        Assert.Contains(("open.bin", XferCode.SessionEnded), failed);
        var statuses = SentStatuses().Concat(replies).ToArray();
        Assert.NotEmpty(statuses);
        Assert.All(statuses, s => Assert.True(s.Status < 0xF0, $"status {s.Status} was put on the wire"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSessionEndingWhileHeldEndsBothSidesAndKeepsThePart(bool bySender)
    {
        wire = frame =>
        {
            if (frame is XferChunk && Interlocked.Increment(ref chunks) == 3)
            {
                Assert.True(bySender ? sender.Pause() : receiver.Pause());
            }

            return frame;
        };

        var send = StartSend(Source("end.bin", RandomNumberGenerator.GetBytes(Chunk * 6)));
        await Until(() => senderHeld.Contains(true) && receiverHeld.Contains(true));
        Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));

        await ended.CancelAsync();
        Assert.Equal(XferCode.SessionEnded, await send.WaitAsync(Patience, TestContext.Current.CancellationToken));
        receiver.Dispose();

        Assert.Equal<(string, XferCode)>([("end.bin", XferCode.SessionEnded)], failed);
        Assert.Single(Directory.GetFiles(Partial));
        Assert.Equal<bool>([true, false], receiverHeld);
        Assert.Equal<bool>([true, false], senderHeld);
        Assert.All(SentStatuses().Concat(replies), s => Assert.True(s.Status < 0xF0));
    }

    [Fact]
    public async Task ASenderCapWithAPeerThatNeverHeardOfTheHoldEndsCancelledAndThePeerIdleDropsKeepingThePart()
    {
        var data = RandomNumberGenerator.GetBytes(Chunk * 6);
        var pausing = true;
        wire = frame =>
        {
            // An old build drops PAUSED and RESUME as unknown.
            if (frame is XferStatus { Status: (byte)XferCode.Paused or (byte)XferCode.Resume })
            {
                return null;
            }

            if (pausing && frame is XferChunk && Interlocked.Increment(ref chunks) == 3)
            {
                Assert.True(sender.Pause());
            }

            return frame;
        };
        var path = Source("one.bin", data);

        var send = StartSend(path);
        await Until(() => SentStatuses().Any(s => s.Status == (byte)XferCode.Paused));
        Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));

        Advance(TransferLimits.PauseCap + TimeSpan.FromSeconds(1));

        Assert.Equal(XferCode.Cancelled, await send.WaitAsync(Patience, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(SentStatuses(), s => s.Status == (byte)XferCode.Cancelled);
        Assert.False(receiver.Busy);
        Assert.Equal<(string, XferCode)>([("one.bin", XferCode.TimedOut)], failed);
        Assert.Single(Directory.GetFiles(Partial));

        pausing = false;
        Assert.Equal(XferCode.Saved, await StartSend(path).WaitAsync(Patience, TestContext.Current.CancellationToken));
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "one.bin")));
    }

    [Fact]
    public async Task APauseAskedBetweenFilesStartsTheNextSendHeld()
    {
        Assert.False(sender.Pause());
        var data = RandomNumberGenerator.GetBytes(Chunk * 3);

        var send = StartSend(Source("pend.bin", data));
        await Until(() => SentStatuses().Any(s => s.Status == (byte)XferCode.Paused));

        Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));
        Assert.Empty(sentFrames.OfType<XferChunk>());
        Assert.Equal<bool>([true], senderHeld);

        Assert.True(sender.Resume());
        Assert.Equal(XferCode.Saved, await send.WaitAsync(Patience, TestContext.Current.CancellationToken));
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(Destination, "pend.bin")));
    }

    [Fact]
    public async Task AResumeBetweenFilesTakesBackAPendingPause()
    {
        Assert.False(sender.Pause());
        Assert.False(sender.Resume());

        Assert.Equal(XferCode.Saved, await StartSend(Source("clear.bin", new byte[50])).WaitAsync(Patience, TestContext.Current.CancellationToken));
        Assert.Empty(senderHeld);
    }

    [Fact]
    public async Task AHeldReceiverEndsAtTheCapOnItsOwnWithNoCallToTouchIt()
    {
        receiver.Take(new XferBegin(1, 100, "own.bin"));
        receiver.Take(new XferChunk(1, 0, new byte[10]));
        Assert.True(receiver.Pause());

        Advance(TransferLimits.PauseCap + TimeSpan.FromSeconds(1));

        await Until(() => !failed.IsEmpty);
        Assert.Equal<(string, XferCode)>([("own.bin", XferCode.Cancelled)], failed);
        Assert.Equal<bool>([true, false], receiverHeld);
        Assert.Single(Directory.GetFiles(Partial));
    }

    [Fact]
    public void ABeginForTheSameHeldFileTakesItOverFromItsOffsetAndAnyOtherIsBusy()
    {
        receiver.Take(new XferBegin(1, 100, "re.bin"));
        receiver.Take(new XferChunk(1, 0, new byte[10]));

        // Not held: a second begin is BUSY, as always.
        replies.Clear();
        receiver.Take(new XferBegin(2, 100, "re.bin"));
        Assert.Equal<XferStatus>([new XferStatus(2, (byte)XferCode.Busy, 0)], replies);

        Assert.True(receiver.Pause());
        replies.Clear();
        receiver.Take(new XferBegin(3, 101, "re.bin"));
        receiver.Take(new XferBegin(4, 100, "other.bin"));
        Assert.Equal<XferStatus>([new XferStatus(3, (byte)XferCode.Busy, 0), new XferStatus(4, (byte)XferCode.Busy, 0)], replies);

        replies.Clear();
        receiver.Take(new XferBegin(5, 100, "re.bin"));
        Assert.Equal<XferStatus>([new XferStatus(5, (byte)XferCode.Ready, 10)], replies);
        Assert.Empty(failed);
        Assert.Equal<bool>([true, false], receiverHeld);
        Assert.True(receiver.Busy);
    }

    public static TheoryData<int, int> StateTable()
    {
        var table = new TheoryData<int, int>();
        for (var state = 0; state < 4; state++)
        {
            for (var action = 0; action < 4; action++)
            {
                table.Add(state, action);
            }
        }

        return table;
    }

    // State: 0 not held, 1 this end holds, 2 the peer holds, 3 both. Event: 0 this end pauses, 1 this end resumes,
    // 2 the peer's PAUSED arrives, 3 the peer's RESUME arrives.
    [Theory]
    [MemberData(nameof(StateTable))]
    public void TheReceiverTableGivesTheSameFinalStateAndAnswersForEveryStateAndEvent(int state, int action)
    {
        receiver.Take(new XferBegin(1, 100, "tab.bin"));
        receiver.Take(new XferChunk(1, 0, new byte[10]));
        var own = state is 1 or 3;
        var peer = state is 2 or 3;
        if (own)
        {
            Assert.True(receiver.Pause());
        }

        if (peer)
        {
            Assert.True(receiver.Take(new XferStatus(1, (byte)XferCode.Paused, 10)));
        }

        var heldBefore = own || peer;
        replies.Clear();
        receiverHeld.Clear();
        var expected = new List<XferStatus>();
        bool result;
        switch (action)
        {
            case 0:
                result = receiver.Pause();
                Assert.Equal(!own, result);
                if (!own)
                {
                    expected.Add(new XferStatus(1, (byte)XferCode.Paused, 10));
                }

                own = true;
                break;
            case 1:
                result = receiver.Resume();
                Assert.Equal(own, result);
                if (own)
                {
                    expected.Add(new XferStatus(1, (byte)XferCode.Resume, 10));
                    if (!peer)
                    {
                        expected.Add(new XferStatus(1, (byte)XferCode.Ready, 10));
                    }
                }

                own = false;
                break;
            case 2:
                Assert.True(receiver.Take(new XferStatus(1, (byte)XferCode.Paused, 10)));
                peer = true;
                break;
            default:
                Assert.True(receiver.Take(new XferStatus(1, (byte)XferCode.Resume, 10)));
                if (!own)
                {
                    expected.Add(new XferStatus(1, (byte)XferCode.Ready, 10));
                }

                peer = false;
                break;
        }

        var held = own || peer;
        Assert.Equal<XferStatus>(expected, replies);
        Assert.Equal<bool>(heldBefore == held ? Array.Empty<bool>() : new[] { held }, receiverHeld);
        Assert.All(replies, s => Assert.True(s.Status < 0xF0));

        // Held, the 30 second idle drop does not run; not held, it does.
        Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(held, receiver.Busy);
    }

    [Theory]
    [MemberData(nameof(StateTable))]
    public async Task TheSenderTableGivesTheSameFinalStateForEveryStateAndEvent(int state, int action)
    {
        wire = _ => null;
        var send = StartSend(Source("st.bin", new byte[10]));
        await Until(() => sentFrames.OfType<XferBegin>().Any());
        var own = state is 1 or 3;
        var peer = state is 2 or 3;
        if (own)
        {
            Assert.True(sender.Pause());
        }

        if (peer)
        {
            Assert.True(sender.Take(new XferStatus(1, (byte)XferCode.Paused, 0)));
        }

        var heldBefore = own || peer;
        senderHeld.Clear();
        switch (action)
        {
            case 0:
                Assert.Equal(!own, sender.Pause());
                own = true;
                break;
            case 1:
                Assert.Equal(own, sender.Resume());
                own = false;
                break;
            case 2:
                Assert.True(sender.Take(new XferStatus(1, (byte)XferCode.Paused, 0)));
                peer = true;
                break;
            default:
                Assert.True(sender.Take(new XferStatus(1, (byte)XferCode.Resume, 0)));
                peer = false;
                break;
        }

        var held = own || peer;
        Assert.Equal<bool>(heldBefore == held ? Array.Empty<bool>() : new[] { held }, senderHeld);

        // Held, the 30 second answer timeout does not run; not held, it does and ends the send.
        Assert.False(await Finishes(send, TimeSpan.FromMilliseconds(200)));
        Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(!held, await Finishes(send, TimeSpan.FromMilliseconds(held ? 400 : 5000)));
        if (held)
        {
            Assert.True(sender.Take(new XferStatus(1, (byte)XferCode.Rejected, 0)));
        }

        Assert.Equal(held ? XferCode.Rejected : XferCode.TimedOut, await send.WaitAsync(Patience, TestContext.Current.CancellationToken));
        Assert.All(SentStatuses(), s => Assert.True(s.Status < 0xF0));
    }

    private Task<XferCode> StartSend(string path, CancellationTokenSource? cancel = null) =>
        Task.Run(() => sender.Send(path, 1, cancel: cancel?.Token ?? TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

    private XferStatus[] SentStatuses() => [.. sentFrames.OfType<XferStatus>()];

    private void Advance(TimeSpan by)
    {
        Interlocked.Add(ref tickMs, (long)by.TotalMilliseconds);
        Interlocked.Add(ref clockTicks, by.Ticks);
    }

    private static async Task Until(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "the condition never held");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<bool> Finishes(Task task, TimeSpan within) =>
        await Task.WhenAny(task, Task.Delay(within, TestContext.Current.CancellationToken)) == task;

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
        sentFrames.Enqueue(sent);
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
