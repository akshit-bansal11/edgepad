namespace Edgepad.Transfer;

/// <summary>Whether a batch has not started, is moving, or has ended, however it ended.</summary>
internal enum TransferPhase { Idle, Running, Finished }

/// <summary>Why one file of a batch did not arrive. Pure: each platform maps it to its own words.</summary>
internal enum TransferFailure { Refused, Damaged, WriteFailed, Busy, TimedOut, LinkLost, Unreadable, Cancelled, NotSent }

/// <summary>Maps the protocol's per-file result codes to <see cref="TransferFailure"/>. Pure.</summary>
internal static class TransferOutcome
{
    /// <summary>How a transfer's final code reads as a failure; null for <see cref="XferCode.Saved"/>.</summary>
    public static TransferFailure? Of(XferCode code) => code switch
    {
        XferCode.Saved => null,
        XferCode.Rejected => TransferFailure.Refused,
        XferCode.BadHash => TransferFailure.Damaged,
        XferCode.WriteFailed => TransferFailure.WriteFailed,
        XferCode.Busy => TransferFailure.Busy,
        XferCode.TimedOut => TransferFailure.TimedOut,
        XferCode.Cancelled => TransferFailure.Cancelled,
        // SessionEnded, and Ready/Progress, which are never a final answer: treat the link as gone.
        _ => TransferFailure.LinkLost,
    };

    /// <summary>
    /// Whether a batch goes on to its next file after this one failed: a fault with this file does not stop the rest,
    /// a fault with the link, the peer's state or the user's cancel does.
    /// </summary>
    public static bool ContinuesAfter(TransferFailure failure) =>
        failure is TransferFailure.Refused or TransferFailure.Damaged or TransferFailure.WriteFailed or TransferFailure.Unreadable;
}

/// <summary>One file of a batch that did not arrive, and why.</summary>
internal sealed record TransferFailed(string Name, TransferFailure Why);

/// <summary>
/// One moment of a batch, for a progress card or toast. <paramref name="Total"/> is 0 when it is not known (receiving: the
/// protocol has no file count), and <paramref name="Index"/> is then a running count. <paramref name="Index"/> is 1-based.
/// </summary>
internal sealed record TransferView(
    TransferPhase Phase,
    bool Sending,
    int Index,
    int Total,
    string Name,
    ulong Sent,
    ulong Size,
    long BytesPerSecond,
    int Done,
    IReadOnlyList<TransferFailed> Failed,
    int Skipped,
    bool Cancelling,
    bool Paused = false)
{
    public static readonly TransferView Idle = new(TransferPhase.Idle, false, 0, 0, "", 0, 0, 0, 0, [], 0, false);
}

/// <summary>
/// Bytes per second as an exponential average over about <see cref="WindowMs"/> of samples. A sample closer than
/// <see cref="MinSampleMs"/> to the last kept one is ignored, so a burst of chunk callbacks costs nothing. Not thread-safe:
/// <see cref="TransferFeed"/> calls it under its own lock.
/// </summary>
internal sealed class SpeedMeter(Func<long>? clock = null)
{
    public const int MinSampleMs = 250;
    public const int WindowMs = 2000;

    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);

    private long? lastAt;
    private ulong lastBytes;
    private double rate;
    private bool hasRate;

    public long BytesPerSecond => hasRate ? (long)rate : 0;

    /// <summary>Takes the running total of bytes moved so far in this file.</summary>
    public void Add(ulong totalBytes)
    {
        var at = now();
        if (lastAt is not { } then || totalBytes < lastBytes)
        {
            lastAt = at;
            lastBytes = totalBytes;
            return;
        }

        var ms = at - then;
        if (ms < MinSampleMs)
        {
            return;
        }

        var sample = (totalBytes - lastBytes) * 1000.0 / ms;
        rate = hasRate ? rate + (Math.Min(1.0, (double)ms / WindowMs) * (sample - rate)) : sample;
        hasRate = true;
        lastAt = at;
        lastBytes = totalBytes;
    }

    public void Reset()
    {
        lastAt = null;
        lastBytes = 0;
        rate = 0;
        hasRate = false;
    }
}

/// <summary>
/// The progress of a batch of transfers, sending or receiving, as a pure model with no UI in it. <see cref="Changed"/> is
/// raised outside the lock, on whichever thread made the change, so a UI handler marshals to its own thread and only that.
/// While bytes move it is raised at most every <see cref="ThrottleMs"/>; any other change raises it at once.
/// </summary>
internal sealed class TransferFeed(Func<long>? clock = null)
{
    public const int ThrottleMs = 250;

    private readonly Lock gate = new();
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);
    private readonly SpeedMeter meter = new(clock);
    private readonly List<TransferFailed> failed = [];

    private TransferPhase phase;
    private bool sending;
    private int index;
    private int total;
    private string name = "";
    private ulong sent;
    private ulong size;
    private int done;
    private int skipped;
    private bool cancelling;
    private bool paused;
    private long? raisedAt;

    public event Action<TransferView>? Changed;

    public TransferView View
    {
        get
        {
            lock (gate)
            {
                return Snapshot();
            }
        }
    }

    /// <summary>Starts a batch, clearing whatever the last one left. <paramref name="total"/> 0 = not known (receiving).</summary>
    public void BeginBatch(bool sending, int total, int skipped = 0) => Change(() =>
    {
        Clear();
        phase = TransferPhase.Running;
        this.sending = sending;
        this.total = total;
        this.skipped = skipped;
        return true;
    });

    /// <summary>More files joined the running batch (Send to again while it runs). Ignored when no batch runs.</summary>
    public void AddToBatch(int more, int skipped = 0) => Change(() =>
    {
        if (phase != TransferPhase.Running)
        {
            return false;
        }

        total += more;
        this.skipped += skipped;
        return true;
    });

    public void StartFile(string name, ulong size) => Change(() =>
    {
        index++;
        this.name = name;
        this.size = size;
        sent = 0;
        meter.Reset();
        meter.Add(0);
        return true;
    });

    /// <summary>The bytes of the current file moved so far. Throttled: cheap to call per chunk.</summary>
    public void Bytes(ulong sent)
    {
        TransferView? view = null;
        lock (gate)
        {
            this.sent = sent;
            meter.Add(sent);
            var at = now();
            if (raisedAt is not { } then || at - then >= ThrottleMs)
            {
                raisedAt = at;
                view = Snapshot();
            }
        }

        if (view is not null)
        {
            Changed?.Invoke(view);
        }
    }

    /// <summary>The file named ended: arrived when <paramref name="failure"/> is null.</summary>
    public void FileDone(string name, TransferFailure? failure) => Change(() =>
    {
        // A file that ended is not held, whatever ended it.
        paused = false;
        if (failure is { } why)
        {
            failed.Add(new TransferFailed(name, why));
        }
        else
        {
            done++;
            sent = size;
        }

        return true;
    });

    /// <summary>The file moving now, or the batch about to start its next, is held (true) or goes on (false). Ignored when no batch runs.</summary>
    public void SetPaused(bool value) => Change(() =>
    {
        if (phase != TransferPhase.Running || paused == value)
        {
            return false;
        }

        paused = value;
        return true;
    });

    public void RequestCancel() => Change(() =>
    {
        if (phase != TransferPhase.Running || cancelling)
        {
            return false;
        }

        cancelling = true;
        return true;
    });

    /// <summary>The batch is over; the view stays, for its results, until <see cref="Dismiss"/>.</summary>
    public void EndBatch() => Change(() =>
    {
        if (phase != TransferPhase.Running)
        {
            return false;
        }

        phase = TransferPhase.Finished;
        return true;
    });

    public void Dismiss() => Change(() =>
    {
        if (phase == TransferPhase.Idle)
        {
            return false;
        }

        Clear();
        return true;
    });

    private void Change(Func<bool> apply)
    {
        TransferView view;
        lock (gate)
        {
            if (!apply())
            {
                return;
            }

            raisedAt = now();
            view = Snapshot();
        }

        Changed?.Invoke(view);
    }

    private void Clear()
    {
        phase = TransferPhase.Idle;
        sending = false;
        index = 0;
        total = 0;
        name = "";
        sent = 0;
        size = 0;
        done = 0;
        skipped = 0;
        cancelling = false;
        paused = false;
        failed.Clear();
        meter.Reset();
    }

    private TransferView Snapshot() => phase == TransferPhase.Idle
        ? TransferView.Idle
        : new(phase, sending, index, total, name, sent, size, meter.BytesPerSecond, done, failed.ToArray(), skipped, cancelling, paused);
}
