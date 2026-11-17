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
    Func<int, bool>? pause = null)
{
    private const int SliceMs = 50;

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

    /// <summary>
    /// Sends <paramref name="path"/> as transfer <paramref name="id"/> (1..255). Returns <see cref="XferCode.Saved"/> when the peer
    /// has the file whole and checked; any other code is how it ended: the peer's own answer, <see cref="XferCode.Cancelled"/> when
    /// <paramref name="cancel"/> fired (the peer is told), <see cref="XferCode.TimedOut"/> or <see cref="XferCode.SessionEnded"/>.
    /// A file that cannot be read, or a send that fails, throws.
    /// </summary>
    public XferCode Send(string path, byte id, CancellationToken cancel = default)
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
            }

            try
            {
                return Run(file, Path.GetFileName(path), id, cancel);
            }
            finally
            {
                lock (gate)
                {
                    sending = false;
                }
            }
        }
    }

    /// <summary>The peer's answer to the file being sent. False when it is not about that file, or not a status this build knows.</summary>
    public bool Take(XferStatus status)
    {
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

    private XferCode Run(FileStream file, string name, byte id, CancellationToken cancel)
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
            if (Await(() => true, id, at, cancel) is { } stopped)
            {
                return stopped;
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
