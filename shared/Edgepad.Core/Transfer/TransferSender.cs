using System.Security.Cryptography;
using Edgepad.Blob;
using Edgepad.Net;
using Edgepad.Protocol;

namespace Edgepad.Transfer;

/// <summary>
/// Sends one file to the peer as XFER_BEGIN, XFER_CHUNKs and XFER_END, and blocks its caller until the peer has answered it,
/// so it is never called on the session's read thread. The statuses that answer come in through <see cref="Take"/>
/// on that read thread. One per session: a second call waits for the first.
/// <para>
/// It starts where the peer says (READY's offset), hashing the bytes before that from disk without sending them. Pacing is
/// <see cref="BlobSender"/>'s: each chunk is its own send, so a PONG gets between any two, a tick's budget by link kind and
/// the smaller busy budget while the phone is sending input. Called again for the same file after a cut link, it resumes.
/// </para>
/// </summary>
internal sealed class TransferSender(
    Action<Frame> send,
    LinkKind link,
    Func<long> lastInputMs,
    CancellationToken ended,
    TimeSpan? answerTimeout = null,
    Func<long>? clock = null,
    Func<int, bool>? pause = null,
    Action<bool>? pausedChanged = null)
{
    private const int SliceMs = 50;

    // A hold is woken by every change to it; the slice only bounds how late a cancel or the session ending is seen.
    private const int HoldSliceMs = 250;

    private readonly Lock lane = new();
    private readonly object gate = new();
    private readonly TimeSpan timeout = answerTimeout ?? TransferLimits.AnswerTimeout;
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);
    private readonly Func<int, bool> wait = pause ?? (ms => !ended.WaitHandle.WaitOne(ms));

    private bool sending;
    private byte activeId;
    private ulong? readyAt;
    private XferCode? final;
    private ulong sentTo;
    private ulong? lastProgress;
    private ulong? rewoundTo;
    private ulong? resendAt;
    private bool endSent;

    // Pause: this end's own hold, whether the peer has been told of it, a RESUME still owed to the peer, the peer's hold,
    // when either began (for the cap), and the last state reported to pausedChanged.
    private bool paused;
    private bool pausedSent;
    private bool resumeOwed;
    private bool peerPaused;
    private long? heldSince;
    private bool lastHeld;

    // A Pause that came when no file was being sent (between a queue's files): the next Send starts held.
    private bool pausePending;
    private readonly object notify = new();

    /// <summary>
    /// Holds the file after the chunk in flight: the peer is told with PAUSED and nothing more is sent, and the answer timeout
    /// stops, until <see cref="Resume"/>. False when no file is being sent, it is already held, or END has gone (the answer is
    /// then only waited for). Safe from any thread; the frame itself is written by the sending thread, never this one.
    /// </summary>
    public bool Pause()
    {
        lock (gate)
        {
            if (!sending)
            {
                // Still False: nothing is held yet. The next Send starts held, so a file dequeued a moment before its Send is not missed.
                pausePending = true;
                return false;
            }

            if (endSent || paused)
            {
                return false;
            }

            paused = true;
            Touch();
            Monitor.PulseAll(gate);
        }

        Notify();
        return true;
    }

    /// <summary>
    /// Lets go of this end's hold: the peer is told with RESUME, and the file goes on from the offset READY then names. While the
    /// peer still holds it, it stays held. False when this end does not hold it.
    /// </summary>
    public bool Resume()
    {
        lock (gate)
        {
            if (!sending)
            {
                pausePending = false;
                return false;
            }

            if (!paused)
            {
                return false;
            }

            paused = false;
            resumeOwed = pausedSent;
            Touch();
            Monitor.PulseAll(gate);
        }

        Notify();
        return true;
    }

    /// <summary>
    /// Sends <paramref name="path"/> as transfer <paramref name="id"/> (1..255). Returns <see cref="XferCode.Saved"/> when the peer
    /// has the file whole and checked; any other code is how it ended: the peer's own answer, <see cref="XferCode.Cancelled"/> when
    /// <paramref name="cancel"/> fired (the peer is told), <see cref="XferCode.TimedOut"/> or <see cref="XferCode.SessionEnded"/>.
    /// A file that cannot be read, or a send that fails, throws.
    /// </summary>
    public XferCode Send(string path, byte id, Action<ulong>? progress = null, CancellationToken cancel = default)
    {
        lock (lane)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            lock (gate)
            {
                activeId = id;
                sending = true;
                readyAt = null;
                final = null;
                sentTo = 0;
                lastProgress = null;
                rewoundTo = null;
                resendAt = null;
                endSent = false;
                ResetPause();
                if (pausePending)
                {
                    pausePending = false;
                    paused = true;
                    Touch();
                }
            }

            Notify();
            try
            {
                return Run(file, Path.GetFileName(path), id, progress, cancel);
            }
            finally
            {
                lock (gate)
                {
                    sending = false;
                    ResetPause();
                }

                Notify();
            }
        }
    }

    /// <summary>The peer's answer to the file being sent. False when it is not about that file, or not a status this build knows.</summary>
    public bool Take(XferStatus status)
    {
        if (status.Status is (byte)XferCode.Paused or (byte)XferCode.Resume)
        {
            return TakePause(status);
        }

        lock (gate)
        {
            if (!sending || status.Id != activeId || status.Status > (byte)XferCode.Busy)
            {
                return false;
            }

            switch ((XferCode)status.Status)
            {
                case XferCode.Ready:
                    readyAt = status.Offset;
                    break;
                case XferCode.Progress:
                    // Right after a rewind, reports still in flight from before it name the offset rewound to or less: not
                    // news, and counted as repeats they would rewind again. A higher one ends that; after END they count.
                    if (rewoundTo is { } floor)
                    {
                        if (status.Offset <= floor && !endSent)
                        {
                            break;
                        }

                        rewoundTo = null;
                    }

                    // A report below what was sent is a lost chunk when it is repeated (every later chunk draws the same
                    // offset) or comes after END; a rising one is only the receiver's pace.
                    if (status.Offset < sentTo && (endSent || status.Offset == lastProgress))
                    {
                        resendAt = status.Offset;
                    }

                    lastProgress = status.Offset;
                    break;
                default:
                    final = (XferCode)status.Status;
                    break;
            }

            Monitor.PulseAll(gate);
            return true;
        }
    }

    // The peer's PAUSED or RESUME for the file being sent.
    private bool TakePause(XferStatus status)
    {
        lock (gate)
        {
            if (!sending || status.Id != activeId)
            {
                return false;
            }

            peerPaused = status.Status == (byte)XferCode.Paused;
            Touch();
            Monitor.PulseAll(gate);
        }

        Notify();
        return true;
    }

    // Under the gate: when a hold began, or null when none holds.
    private void Touch() => heldSince = paused || peerPaused ? heldSince ?? now() : null;

    // Under the gate.
    private void ResetPause()
    {
        paused = false;
        pausedSent = false;
        resumeOwed = false;
        peerPaused = false;
        heldSince = null;
    }

    // Outside the gate: tells the owner when the transfer starts or stops being held by either end.
    // The state is read and delivered under one lock, so the owner sees the changes in the order they happened.
    private void Notify()
    {
        lock (notify)
        {
            bool held;
            lock (gate)
            {
                held = paused || peerPaused;
                if (held == lastHeld)
                {
                    return;
                }

                lastHeld = held;
            }

            pausedChanged?.Invoke(held);
        }
    }

    private XferCode Run(FileStream file, string name, byte id, Action<ulong>? progress, CancellationToken cancel)
    {
        var size = (ulong)file.Length;
        if (size > TransferLimits.MaxBytes)
        {
            return XferCode.Rejected;
        }

        send(new XferBegin(id, size, name));
        if (Await(() => readyAt is not null, id, 0, cancel) is { } early)
        {
            return early;
        }

        var offset = readyAt!.Value;
        if (offset > size)
        {
            send(new XferStatus(id, (byte)XferCode.Cancelled, 0));
            return XferCode.Rejected;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[FrameCodec.MaxXferChunkBytes];
        if (!HashHead(file, hash, buffer, offset))
        {
            return XferCode.WriteFailed;
        }

        var sentInTick = 0;
        var at = offset;
        while (true)
        {
            // A hold is honoured here, between chunks and never inside one.
            var stopped = Hold(id, at, cancel) ?? Await(() => true, id, at, cancel);
            if (stopped is { } code)
            {
                return code;
            }

            // The receiver said, twice or after END, that it holds less than was sent: a chunk was lost. Go back to what it holds.
            if (TakeResend() is { } from)
            {
                file.Position = 0;
                hash.GetHashAndReset();
                if (!HashHead(file, hash, buffer, from))
                {
                    return XferCode.WriteFailed;
                }

                at = from;
            }

            if (at >= size)
            {
                SetEndSent(true);
                send(new XferEnd(id, hash.GetHashAndReset()));
                if (Await(() => resendAt is not null, id, size, cancel) is { } done)
                {
                    return done;
                }

                SetEndSent(false);
                continue;
            }

            var count = file.Read(buffer, 0, (int)Math.Min((ulong)buffer.Length, size - at));
            if (count == 0)
            {
                send(new XferStatus(id, (byte)XferCode.Cancelled, at));
                return XferCode.WriteFailed;
            }

            hash.AppendData(buffer.AsSpan(0, count));
            SetSentTo(at + (ulong)count);
            send(new XferChunk(id, at, buffer[..count]));
            at += (ulong)count;
            progress?.Invoke(at);
            if (++sentInTick >= Budget() && at < size)
            {
                sentInTick = 0;
                if (!wait(BlobSender.TickMs))
                {
                    return XferCode.SessionEnded;
                }
            }
        }
    }

    /// <summary>
    /// Null when no end holds the file, or when it was held and has been let go and the peer's READY has named where to
    /// go on (a READY below <paramref name="at"/> is a rewind, taken like a lost chunk); otherwise how it ended. While held
    /// the answer timeout does not run. A hold older than <see cref="TransferLimits.PauseCap"/> ends the send as
    /// <see cref="XferCode.Cancelled"/> without telling the peer, which has the same cap and so keeps its part; a user's
    /// cancel tells it, as everywhere else.
    /// </summary>
    private XferCode? Hold(byte id, ulong at, CancellationToken cancel)
    {
        lock (gate)
        {
            if (!paused && !peerPaused && !resumeOwed)
            {
                return null;
            }

            readyAt = null;
        }

        var cancelled = false;
        while (!cancelled)
        {
            XferStatus? tell = null;
            lock (gate)
            {
                if (final is { } answer)
                {
                    return answer;
                }

                if (ended.IsCancellationRequested)
                {
                    return XferCode.SessionEnded;
                }

                if (cancel.IsCancellationRequested)
                {
                    cancelled = true;
                }
                else if (paused && !pausedSent)
                {
                    pausedSent = true;
                    tell = new XferStatus(id, (byte)XferCode.Paused, at);
                }
                else if (!paused && resumeOwed)
                {
                    resumeOwed = false;
                    pausedSent = false;
                    readyAt = null;
                    tell = new XferStatus(id, (byte)XferCode.Resume, at);
                }
                else if (!paused && !peerPaused)
                {
                    break;
                }
                else if (heldSince is { } since && now() - since >= (long)TransferLimits.PauseCap.TotalMilliseconds)
                {
                    return XferCode.Cancelled;
                }
                else
                {
                    Monitor.Wait(gate, HoldSliceMs);
                }
            }

            // Outside the lock: a write to a slow socket must not keep the read thread from delivering statuses.
            if (tell is not null)
            {
                send(tell);
            }
        }

        // Released, or cancelled: a cancel goes the way a cancel always does, and tells the peer.
        if (cancelled)
        {
            return Await(() => true, id, at, cancel);
        }

        if (Await(() => readyAt is not null, id, at, cancel) is { } over)
        {
            return over;
        }

        lock (gate)
        {
            if (readyAt is { } held && held < at)
            {
                resendAt = held;
            }
        }

        return null;
    }

    /// <summary>Hashes the first <paramref name="upTo"/> bytes from the file's start without sending them. False when the file is shorter.</summary>
    private static bool HashHead(FileStream file, IncrementalHash hash, byte[] buffer, ulong upTo)
    {
        for (var done = 0UL; done < upTo;)
        {
            var read = file.Read(buffer, 0, (int)Math.Min((ulong)buffer.Length, upTo - done));
            if (read == 0)
            {
                return false;
            }

            hash.AppendData(buffer.AsSpan(0, read));
            done += (ulong)read;
        }

        return true;
    }

    private ulong? TakeResend()
    {
        lock (gate)
        {
            var from = resendAt;
            resendAt = null;
            sentTo = from ?? sentTo;
            if (from is not null)
            {
                lastProgress = null;
                rewoundTo = from;
            }

            return from;
        }
    }

    private void SetSentTo(ulong value)
    {
        lock (gate)
        {
            sentTo = value;
        }
    }

    private void SetEndSent(bool value)
    {
        lock (gate)
        {
            endSent = value;
        }
    }

    /// <summary>
    /// Null when <paramref name="ready"/> holds and nothing has ended the transfer; otherwise how it ended. With a condition
    /// that never holds this waits for the peer's final answer, or the timeout.
    /// </summary>
    private XferCode? Await(Func<bool> ready, byte id, ulong at, CancellationToken cancel)
    {
        var deadline = now() + (long)timeout.TotalMilliseconds;
        var timedOut = false;
        lock (gate)
        {
            while (!cancel.IsCancellationRequested)
            {
                if (final is { } answer)
                {
                    return answer;
                }

                if (ended.IsCancellationRequested)
                {
                    return XferCode.SessionEnded;
                }

                if (ready())
                {
                    return null;
                }

                // Held by either end: the wait starts over, until the hold has run out its cap.
                // Past the cap it ends as CANCELLED without telling the peer, as in Hold: its CANCELLED would delete the part.
                if (heldSince is { } since)
                {
                    if (now() - since >= (long)TransferLimits.PauseCap.TotalMilliseconds)
                    {
                        return XferCode.Cancelled;
                    }

                    deadline = now() + (long)timeout.TotalMilliseconds;
                }

                if (now() >= deadline)
                {
                    timedOut = true;
                    break;
                }

                Monitor.Wait(gate, SliceMs);
            }
        }

        // Outside the lock: a write to a slow socket must not keep the read thread from delivering statuses. A
        // timeout says CANCELLED too, so the receiver does not sit on the file answering BUSY.
        send(new XferStatus(id, (byte)XferCode.Cancelled, at));
        return timedOut ? XferCode.TimedOut : XferCode.Cancelled;
    }

    private int Budget()
    {
        if (now() - lastInputMs() < BlobSender.InputQuietMs)
        {
            return BlobSender.BusyChunksPerTick;
        }

        return link == LinkKind.Bluetooth ? BlobSender.BluetoothChunksPerTick : BlobSender.NetworkChunksPerTick;
    }
}
