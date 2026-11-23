namespace Edgepad.Transfer;

/// <summary>
/// Sends a batch of files one after another, because the protocol holds one transfer per direction at a time, and
/// reports the batch to a <see cref="TransferFeed"/>. Files added while it runs join the same batch, in order.
/// <para>
/// <c>sendOne(path, size, cancel)</c> sends one file and returns its final code, or null when there is no phone; it may throw
/// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> for a file it cannot read. The caller feeds
/// <see cref="TransferFeed.Bytes"/> from the sender's progress callback. <c>sizeOf(path)</c> returns null, or throws an IO
/// error, for a file that is gone or cannot be read. A per-file failure lets the batch go on (<see cref="TransferOutcome.ContinuesAfter"/>); any
/// other stops it, and every file left is listed as <see cref="TransferFailure.NotSent"/> (<see cref="TransferFailure.LinkLost"/>
/// when there was no phone).
/// </para>
/// </summary>
internal sealed class SendQueue(
    Func<string, ulong, CancellationToken, Task<XferCode?>> sendOne,
    TransferFeed feed,
    Func<string, long?> sizeOf,
    Func<bool>? pauseCurrent = null,
    Func<bool>? resumeCurrent = null,
    CancellationToken sessionEnded = default)
{
    private readonly Lock gate = new();
    private readonly Queue<string> pending = new();
    private readonly List<string> failedPaths = [];

    private bool running;
    private bool cancelled;
    private TaskCompletionSource? held;
    private Action? cancelCurrent;
    private Task completion = Task.CompletedTask;

    public bool IsRunning
    {
        get
        {
            lock (gate)
            {
                return running;
            }
        }
    }

    /// <summary>Ends when the batch running now does; already complete when none runs. Never faults.</summary>
    public Task Completion
    {
        get
        {
            lock (gate)
            {
                return completion;
            }
        }
    }

    /// <summary>The files of the last batch that did not arrive, failed or not sent, in order, for a Retry.</summary>
    public IReadOnlyList<string> FailedPaths
    {
        get
        {
            lock (gate)
            {
                return failedPaths.ToArray();
            }
        }
    }

    /// <summary>Starts a batch when none runs; otherwise adds to the one running. <paramref name="skipped"/> is shown only.</summary>
    public void Enqueue(IReadOnlyList<string> paths, int skipped = 0)
    {
        lock (gate)
        {
            foreach (var path in paths)
            {
                pending.Enqueue(path);
            }

            if (running)
            {
                feed.AddToBatch(paths.Count, skipped);
                return;
            }

            running = true;
            cancelled = false;
            failedPaths.Clear();
            feed.BeginBatch(sending: true, paths.Count, skipped);
            completion = Task.Run(Run, CancellationToken.None);
        }
    }

    /// <summary>Whether the batch is held: the file being sent is (<c>pauseCurrent</c>) and the next one does not start.</summary>
    public bool IsPaused
    {
        get
        {
            lock (gate)
            {
                return held is not null;
            }
        }
    }

    /// <summary>
    /// Holds the batch: <c>pauseCurrent</c> holds the file being sent, and no next file starts until <see cref="Resume"/>.
    /// False when no batch runs, it is being cancelled, or it is held already.
    /// </summary>
    public bool Pause()
    {
        lock (gate)
        {
            if (!running || cancelled || held is not null)
            {
                return false;
            }

            held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        pauseCurrent?.Invoke();
        return true;
    }

    /// <summary>Lets the batch go on. False when it is not held.</summary>
    public bool Resume()
    {
        TaskCompletionSource? release;
        lock (gate)
        {
            release = held;
            held = null;
        }

        if (release is null)
        {
            return false;
        }

        release.TrySetResult();
        resumeCurrent?.Invoke();
        return true;
    }

    /// <summary>Cancels the file being sent and drops the rest as not sent. Nothing when no batch runs.</summary>
    public void Cancel()
    {
        Action? stop;
        TaskCompletionSource? release;
        lock (gate)
        {
            if (!running || cancelled)
            {
                return;
            }

            cancelled = true;
            stop = cancelCurrent;
            release = held;
            held = null;
            feed.RequestCancel();
        }

        // A held batch is let go so its loop sees the cancel; the file in flight sees its token.
        release?.TrySetResult();

        // Outside the gate: cancelling can run the sender's continuation on this thread.
        try
        {
            stop?.Invoke();
        }
        catch (ObjectDisposedException)
        {
            // That file ended and its token went with it; the loop sees `cancelled` before the next one.
        }
    }

    private async Task Run()
    {
        TransferFailure? rest = null;
        string? current = null;
        try
        {
            while (true)
            {
                string path;
                using var cancel = new CancellationTokenSource();
                Task? hold = null;
                lock (gate)
                {
                    if (cancelled)
                    {
                        rest ??= TransferFailure.NotSent;
                    }

                    if (rest is { } why)
                    {
                        Drain(why);
                    }

                    if (pending.Count == 0)
                    {
                        Finish();
                        return;
                    }

                    // Held: the next file waits here, outside the gate, and nothing of it starts.
                    hold = held?.Task;
                    path = hold is null ? pending.Dequeue() : "";
                    if (hold is null)
                    {
                        current = path;
                        cancelCurrent = cancel.Cancel;
                    }
                }

                if (hold is not null)
                {
                    feed.SetPaused(true);

                    // Held between files too long, or the session ended under the hold: the rest is not sent, as for a cancel.
                    using var watch = CancellationTokenSource.CreateLinkedTokenSource(sessionEnded);
                    var expiry = Task.Delay(TransferLimits.PauseCap, watch.Token);
                    var over = await Task.WhenAny(hold, expiry).ConfigureAwait(false) != hold;
                    await watch.CancelAsync().ConfigureAwait(false);
                    if (over)
                    {
                        lock (gate)
                        {
                            held = null;
                            rest ??= TransferFailure.NotSent;
                        }

                        resumeCurrent?.Invoke();
                    }

                    feed.SetPaused(false);
                    continue;
                }

                var name = Path.GetFileName(path);
                var failure = await SendOne(path, name, cancel.Token).ConfigureAwait(false);
                lock (gate)
                {
                    cancelCurrent = null;
                    current = null;
                    if (failure is { } why)
                    {
                        failedPaths.Add(path);
                        if (!TransferOutcome.ContinuesAfter(why))
                        {
                            rest = why == TransferFailure.LinkLost ? TransferFailure.LinkLost : TransferFailure.NotSent;
                        }
                    }
                }

                feed.FileDone(name, failure);
            }
        }
        catch (Exception e)
        {
            // Not a file's fault: sendOne broke. The batch stops; the type and message are logged, never the path.
            Log.Write($"Send queue: {e.GetType().Name}: {e.Message}");
            lock (gate)
            {
                cancelCurrent = null;
                if (current is not null)
                {
                    failedPaths.Add(current);
                    feed.FileDone(Path.GetFileName(current), TransferFailure.NotSent);
                }

                Drain(TransferFailure.NotSent);
                Finish();
            }
        }
    }

    private async Task<TransferFailure?> SendOne(string path, string name, CancellationToken cancel)
    {
        // A file can go between the Exists and the Length: that is this file unreadable, not the batch broken.
        long? known;
        try
        {
            known = sizeOf(path);
        }
        catch (Exception e)
        {
            // Any fault of the probe is this one file's; the type alone is logged, never the path.
            if (e is not (IOException or UnauthorizedAccessException))
            {
                Log.Write($"Send queue: size of a file: {e.GetType().Name}");
            }

            known = null;
        }

        // Started even when unreadable, so the index still counts every file of the batch.
        if (known is not { } length || length < 0)
        {
            feed.StartFile(name, 0);
            return TransferFailure.Unreadable;
        }

        var size = (ulong)length;
        feed.StartFile(name, size);
        try
        {
            return await sendOne(path, size, cancel).ConfigureAwait(false) is { } code
                ? TransferOutcome.Of(code)
                : TransferFailure.LinkLost;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return TransferFailure.Unreadable;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return TransferFailure.Cancelled;
        }
    }

    // Under the gate. Every file still waiting ends as `why`.
    private void Drain(TransferFailure why)
    {
        while (pending.TryDequeue(out var path))
        {
            failedPaths.Add(path);
            feed.FileDone(Path.GetFileName(path), why);
        }
    }

    // Under the gate, so an Enqueue cannot slip in between the last file and the end and be lost.
    private void Finish()
    {
        running = false;
        held = null;
        feed.EndBatch();
    }
}
