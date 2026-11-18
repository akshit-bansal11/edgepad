namespace Edgepad.Transfer;

/// <summary>
/// Sends a batch of files one after another, because the protocol holds one transfer per direction at a time, and
/// reports the batch to a <see cref="TransferFeed"/>. Files added while it runs join the same batch, in order.
/// <para>
/// <c>sendOne(path, size, cancel)</c> sends one file and returns its final code, or null when there is no phone; it may throw
/// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> for a file it cannot read. The caller feeds
/// <see cref="TransferFeed.Bytes"/> from the sender's progress callback. <c>sizeOf(path)</c> returns null for a file that
/// is gone or cannot be read. A per-file failure lets the batch go on (<see cref="TransferOutcome.ContinuesAfter"/>); any
/// other stops it, and every file left is listed as <see cref="TransferFailure.NotSent"/> (<see cref="TransferFailure.LinkLost"/>
/// when there was no phone).
/// </para>
/// </summary>
internal sealed class SendQueue(
    Func<string, ulong, CancellationToken, Task<XferCode?>> sendOne, TransferFeed feed, Func<string, long?> sizeOf)
{
    private readonly Lock gate = new();
    private readonly Queue<string> pending = new();
    private readonly List<string> failedPaths = [];

    private bool running;
    private bool cancelled;
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

    /// <summary>Cancels the file being sent and drops the rest as not sent. Nothing when no batch runs.</summary>
    public void Cancel()
    {
        Action? stop;
        lock (gate)
        {
            if (!running || cancelled)
            {
                return;
            }

            cancelled = true;
            stop = cancelCurrent;
            feed.RequestCancel();
        }

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

                    path = pending.Dequeue();
                    current = path;
                    cancelCurrent = cancel.Cancel;
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
        // Started even when unreadable, so the index still counts every file of the batch.
        if (sizeOf(path) is not { } length || length < 0)
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
        feed.EndBatch();
    }
}
