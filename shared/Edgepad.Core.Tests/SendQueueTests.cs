using Edgepad.Transfer;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The batch sender with a scripted <c>sendOne</c>: each file's answer is set per test, and a file can be held open on a
/// gate so the test can cancel or add files while it is "sending".
/// </summary>
public sealed class SendQueueTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Lock gate = new();
    private readonly List<string> sentOrder = [];
    private readonly Dictionary<string, Func<CancellationToken, Task<XferCode?>>> script = [];
    private readonly HashSet<string> unreadable = [];
    private readonly HashSet<string> vanishing = [];
    private readonly HashSet<string> broken = [];
    private readonly TransferFeed feed = new();
    private readonly SendQueue queue;

    public SendQueueTests()
    {
        queue = new SendQueue(SendOne, feed, SizeOf);
    }

    [Fact]
    public async Task AllSavedEndsWithEveryFileDone()
    {
        queue.Enqueue(["a.txt", "b.txt", "c.txt"]);
        await Finished();

        var view = feed.View;
        Assert.Equal(TransferPhase.Finished, view.Phase);
        Assert.Equal(3, view.Done);
        Assert.Equal(3, view.Total);
        Assert.Equal(3, view.Index);
        Assert.Empty(view.Failed);
        Assert.Empty(queue.FailedPaths);
        Assert.False(queue.IsRunning);
        Assert.Equal(["a.txt", "b.txt", "c.txt"], Sent());
    }

    [Fact]
    public async Task ARefusedFileDoesNotStopTheBatch()
    {
        Answer("b.txt", XferCode.Rejected);
        queue.Enqueue(["a.txt", "b.txt", "c.txt"]);
        await Finished();

        Assert.Equal(2, feed.View.Done);
        Assert.Equal([new TransferFailed("b.txt", TransferFailure.Refused)], feed.View.Failed);
        Assert.Equal(["b.txt"], queue.FailedPaths);
        Assert.Equal(["a.txt", "b.txt", "c.txt"], Sent());
    }

    [Fact]
    public async Task CancelMidBatchDropsTheRestAsNotSent()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        script["b.txt"] = async cancel =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancel);
            }
            catch (OperationCanceledException)
            {
                return XferCode.Cancelled;
            }

            return XferCode.Saved;
        };

        queue.Enqueue(["a.txt", "b.txt", "c.txt", "d.txt"]);
        await started.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        queue.Cancel();
        await Finished();

        var view = feed.View;
        Assert.True(view.Cancelling);
        Assert.Equal(1, view.Done);
        Assert.Equal(
            [
                new TransferFailed("b.txt", TransferFailure.Cancelled),
                new TransferFailed("c.txt", TransferFailure.NotSent),
                new TransferFailed("d.txt", TransferFailure.NotSent),
            ],
            view.Failed);
        Assert.Equal(["b.txt", "c.txt", "d.txt"], queue.FailedPaths);
        Assert.Equal(["a.txt", "b.txt"], Sent());
    }

    [Fact]
    public async Task NoPhoneMarksThisAndTheRestLinkLost()
    {
        script["b.txt"] = _ => Task.FromResult<XferCode?>(null);
        queue.Enqueue(["a.txt", "b.txt", "c.txt"]);
        await Finished();

        Assert.Equal(1, feed.View.Done);
        Assert.Equal(
            [new TransferFailed("b.txt", TransferFailure.LinkLost), new TransferFailed("c.txt", TransferFailure.LinkLost)],
            feed.View.Failed);
        Assert.Equal(["a.txt", "b.txt"], Sent());
    }

    [Fact]
    public async Task ABusyPeerStopsTheBatch()
    {
        Answer("a.txt", XferCode.Busy);
        queue.Enqueue(["a.txt", "b.txt"]);
        await Finished();

        Assert.Equal(
            [new TransferFailed("a.txt", TransferFailure.Busy), new TransferFailed("b.txt", TransferFailure.NotSent)],
            feed.View.Failed);
        Assert.Equal(["a.txt"], Sent());
    }

    [Fact]
    public async Task UnreadableFilesAreSkippedAndTheBatchGoesOn()
    {
        unreadable.Add("b.txt");
        script["c.txt"] = _ => throw new IOException("locked");
        queue.Enqueue(["a.txt", "b.txt", "c.txt", "d.txt"]);
        await Finished();

        var view = feed.View;
        Assert.Equal(2, view.Done);
        Assert.Equal(4, view.Index);
        Assert.Equal(
            [new TransferFailed("b.txt", TransferFailure.Unreadable), new TransferFailed("c.txt", TransferFailure.Unreadable)],
            view.Failed);
        Assert.Equal(["b.txt", "c.txt"], queue.FailedPaths);
        Assert.Equal(["a.txt", "c.txt", "d.txt"], Sent());
    }

    [Fact]
    public async Task ASizeProbeThatThrowsMarksOnlyThatFileUnreadable()
    {
        vanishing.Add("b.txt");
        broken.Add("c.txt");
        queue.Enqueue(["a.txt", "b.txt", "c.txt", "d.txt"]);
        await Finished();

        var view = feed.View;
        Assert.Equal(2, view.Done);
        Assert.Equal(4, view.Index);
        Assert.Equal(
            [new TransferFailed("b.txt", TransferFailure.Unreadable), new TransferFailed("c.txt", TransferFailure.Unreadable)],
            view.Failed);
        Assert.Equal(["b.txt", "c.txt"], queue.FailedPaths);
        Assert.Equal(["a.txt", "d.txt"], Sent());
    }

    [Fact]
    public async Task FailedPathsHoldFailedAndNotSentInOrder()
    {
        Answer("a.txt", XferCode.BadHash);
        Answer("c.txt", XferCode.TimedOut);
        queue.Enqueue(["a.txt", "b.txt", "c.txt", "d.txt", "e.txt"]);
        await Finished();

        Assert.Equal(["a.txt", "c.txt", "d.txt", "e.txt"], queue.FailedPaths);
    }

    [Fact]
    public async Task EnqueueWhileRunningJoinsTheSameBatchInOrder()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<XferCode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        script["a.txt"] = _ =>
        {
            started.SetResult();
            return release.Task;
        };

        queue.Enqueue(["a.txt", "b.txt"]);
        await started.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.True(queue.IsRunning);
        queue.Enqueue(["c.txt", "d.txt"], skipped: 1);
        Assert.Equal(4, feed.View.Total);
        Assert.Equal(1, feed.View.Skipped);
        release.SetResult(XferCode.Saved);
        await Finished();

        Assert.Equal(4, feed.View.Done);
        Assert.Equal(["a.txt", "b.txt", "c.txt", "d.txt"], Sent());
    }

    [Fact]
    public async Task AnUnexpectedThrowStopsTheBatchAndStillEndsIt()
    {
        script["b.txt"] = _ => throw new InvalidOperationException("broken");
        queue.Enqueue(["a.txt", "b.txt", "c.txt"]);
        await Finished();

        var view = feed.View;
        Assert.Equal(TransferPhase.Finished, view.Phase);
        Assert.Equal(1, view.Done);
        Assert.Equal(
            [new TransferFailed("b.txt", TransferFailure.NotSent), new TransferFailed("c.txt", TransferFailure.NotSent)],
            view.Failed);
        Assert.False(queue.IsRunning);
        Assert.Equal(["b.txt", "c.txt"], queue.FailedPaths);
    }

    [Fact]
    public async Task ANewBatchStartsCleanAfterTheLast()
    {
        Answer("a.txt", XferCode.Rejected);
        queue.Enqueue(["a.txt"]);
        await Finished();
        Assert.Equal(["a.txt"], queue.FailedPaths);

        script.Clear();
        queue.Enqueue(["a.txt"]);
        await Finished();

        Assert.Empty(queue.FailedPaths);
        Assert.Equal(1, feed.View.Done);
        Assert.Empty(feed.View.Failed);
    }

    [Fact]
    public void CancelWithNothingRunningDoesNothing()
    {
        queue.Cancel();

        Assert.False(queue.IsRunning);
        Assert.Equal(TransferPhase.Idle, feed.View.Phase);
        Assert.True(queue.Completion.IsCompleted);
    }

    private Task<XferCode?> SendOne(string path, ulong size, CancellationToken cancel)
    {
        Assert.Equal(100UL, size);
        lock (gate)
        {
            sentOrder.Add(path);
        }

        return script.TryGetValue(path, out var answer) ? answer(cancel) : Task.FromResult<XferCode?>(XferCode.Saved);
    }

    private long? SizeOf(string path)
    {
        if (vanishing.Contains(path))
        {
            throw new FileNotFoundException("gone", path);
        }

        if (broken.Contains(path))
        {
            throw new InvalidOperationException("probe broke");
        }

        return unreadable.Contains(path) ? null : 100;
    }

    private void Answer(string path, XferCode code) => script[path] = _ => Task.FromResult<XferCode?>(code);

    private string[] Sent()
    {
        lock (gate)
        {
            return [.. sentOrder];
        }
    }

    private Task Finished() => queue.Completion.WaitAsync(Patience, TestContext.Current.CancellationToken);
}
