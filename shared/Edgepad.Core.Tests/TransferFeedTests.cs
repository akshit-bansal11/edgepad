using Edgepad.Transfer;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The batch progress model and its speed meter, on a fake clock, so nothing here sleeps.</summary>
public sealed class TransferFeedTests
{
    private readonly List<TransferView> raised = [];
    private readonly TransferFeed feed;
    private long now = 1_000_000;

    public TransferFeedTests()
    {
        feed = new TransferFeed(() => now);
        feed.Changed += raised.Add;
    }

    [Fact]
    public void IdleUntilABatchBegins()
    {
        Assert.Same(TransferView.Idle, feed.View);
        Assert.Equal(TransferPhase.Idle, feed.View.Phase);
    }

    [Fact]
    public void ThreeFilesStepIndexAgainstTotal()
    {
        feed.BeginBatch(sending: true, total: 3, skipped: 1);
        foreach (var (name, i) in new[] { ("a.txt", 1), ("b.txt", 2), ("c.txt", 3) })
        {
            feed.StartFile(name, 100);
            Assert.Equal(i, feed.View.Index);
            Assert.Equal(3, feed.View.Total);
            Assert.Equal(name, feed.View.Name);
            Assert.Equal(0UL, feed.View.Sent);
            feed.FileDone(name, null);
            Assert.Equal(100UL, feed.View.Sent);
        }

        feed.EndBatch();
        var view = feed.View;
        Assert.Equal(TransferPhase.Finished, view.Phase);
        Assert.True(view.Sending);
        Assert.Equal(3, view.Done);
        Assert.Empty(view.Failed);
        Assert.Equal(1, view.Skipped);
    }

    [Fact]
    public void FailuresAreListedByName()
    {
        feed.BeginBatch(sending: true, total: 2);
        feed.StartFile("a.txt", 10);
        feed.FileDone("a.txt", TransferFailure.Refused);
        feed.FileDone("b.txt", TransferFailure.NotSent);

        TransferFailed[] expected =
            [new("a.txt", TransferFailure.Refused), new("b.txt", TransferFailure.NotSent)];
        Assert.Equal(0, feed.View.Done);
        Assert.Equal(expected, feed.View.Failed);
    }

    [Fact]
    public void ReceivingHasNoTotal()
    {
        feed.BeginBatch(sending: false, total: 0);
        feed.StartFile("a.txt", 10);
        feed.StartFile("b.txt", 10);

        Assert.False(feed.View.Sending);
        Assert.Equal(0, feed.View.Total);
        Assert.Equal(2, feed.View.Index);
    }

    [Fact]
    public void AddToBatchGrowsTheTotalOnlyWhileRunning()
    {
        feed.AddToBatch(5);
        Assert.Equal(TransferPhase.Idle, feed.View.Phase);

        feed.BeginBatch(sending: true, total: 2);
        feed.AddToBatch(3, skipped: 2);
        Assert.Equal(5, feed.View.Total);
        Assert.Equal(2, feed.View.Skipped);

        feed.EndBatch();
        feed.AddToBatch(1);
        Assert.Equal(5, feed.View.Total);
    }

    [Fact]
    public void BytesAreThrottledButStateChangesAreNot()
    {
        feed.BeginBatch(sending: true, total: 1);
        feed.StartFile("a.txt", 1_000_000);
        var before = raised.Count;

        for (var i = 1; i <= 1000; i++)
        {
            if (i % 10 == 0)
            {
                now++;
            }

            feed.Bytes((ulong)i * 1000);
        }

        Assert.True(raised.Count - before <= 1);
        Assert.Equal(1_000_000UL, feed.View.Sent);

        var mid = raised.Count;
        feed.FileDone("a.txt", null);
        Assert.Equal(mid + 1, raised.Count);
        Assert.Equal(1, raised[^1].Done);
    }

    [Fact]
    public void BytesRaiseAgainOnceTheThrottleHasPassed()
    {
        feed.BeginBatch(sending: true, total: 1);
        feed.StartFile("a.txt", 1000);
        var before = raised.Count;

        now += TransferFeed.ThrottleMs;
        feed.Bytes(500);

        Assert.Equal(before + 1, raised.Count);
        Assert.Equal(500UL, raised[^1].Sent);
    }

    [Fact]
    public void CancelIsShownAndKeptToTheEnd()
    {
        feed.RequestCancel();
        Assert.False(feed.View.Cancelling);

        feed.BeginBatch(sending: true, total: 2);
        feed.RequestCancel();
        var count = raised.Count;
        feed.RequestCancel();
        Assert.Equal(count, raised.Count);
        feed.EndBatch();

        Assert.True(feed.View.Cancelling);
        Assert.Equal(TransferPhase.Finished, feed.View.Phase);
    }

    [Fact]
    public void DismissReturnsToIdleAndANewBatchStartsClean()
    {
        feed.BeginBatch(sending: true, total: 1);
        feed.FileDone("a.txt", TransferFailure.Busy);
        feed.EndBatch();
        feed.Dismiss();
        Assert.Same(TransferView.Idle, feed.View);

        feed.BeginBatch(sending: true, total: 1);
        Assert.Empty(feed.View.Failed);
        Assert.Equal(0, feed.View.Index);
    }

    [Fact]
    public void SpeedMeterReadsASteadyRate()
    {
        var meter = new SpeedMeter(() => now);
        meter.Add(0);
        for (var step = 1; step <= 30; step++)
        {
            now += 100;
            meter.Add((ulong)step * 100_000);
        }

        Assert.InRange(meter.BytesPerSecond, 990_000, 1_010_000);
    }

    [Fact]
    public void SpeedMeterIgnoresSamplesTooCloseTogether()
    {
        var meter = new SpeedMeter(() => now);
        meter.Add(0);
        now += SpeedMeter.MinSampleMs - 1;
        meter.Add(1_000_000);

        Assert.Equal(0, meter.BytesPerSecond);

        now += 1;
        meter.Add(1_000_000);
        Assert.Equal(4_000_000, meter.BytesPerSecond);
    }

    [Fact]
    public void SpeedMeterResets()
    {
        var meter = new SpeedMeter(() => now);
        meter.Add(0);
        now += 1000;
        meter.Add(1000);
        Assert.Equal(1000, meter.BytesPerSecond);

        meter.Reset();
        Assert.Equal(0, meter.BytesPerSecond);
    }

    [Theory]
    [InlineData((int)XferCode.Rejected, (int)TransferFailure.Refused)]
    [InlineData((int)XferCode.BadHash, (int)TransferFailure.Damaged)]
    [InlineData((int)XferCode.WriteFailed, (int)TransferFailure.WriteFailed)]
    [InlineData((int)XferCode.Busy, (int)TransferFailure.Busy)]
    [InlineData((int)XferCode.TimedOut, (int)TransferFailure.TimedOut)]
    [InlineData((int)XferCode.Cancelled, (int)TransferFailure.Cancelled)]
    [InlineData((int)XferCode.SessionEnded, (int)TransferFailure.LinkLost)]
    [InlineData((int)XferCode.Ready, (int)TransferFailure.LinkLost)]
    [InlineData((int)XferCode.Progress, (int)TransferFailure.LinkLost)]
    public void EachCodeReadsAsAFailure(int code, int expected) =>
        Assert.Equal((TransferFailure)expected, TransferOutcome.Of((XferCode)code));

    [Fact]
    public void SavedIsNoFailureAndEveryCodeIsMapped()
    {
        Assert.Null(TransferOutcome.Of(XferCode.Saved));
        foreach (var code in Enum.GetValues<XferCode>())
        {
            Assert.Equal(code == XferCode.Saved, TransferOutcome.Of(code) is null);
        }
    }

    [Theory]
    [InlineData((int)TransferFailure.Refused, true)]
    [InlineData((int)TransferFailure.Damaged, true)]
    [InlineData((int)TransferFailure.WriteFailed, true)]
    [InlineData((int)TransferFailure.Unreadable, true)]
    [InlineData((int)TransferFailure.Busy, false)]
    [InlineData((int)TransferFailure.TimedOut, false)]
    [InlineData((int)TransferFailure.LinkLost, false)]
    [InlineData((int)TransferFailure.Cancelled, false)]
    [InlineData((int)TransferFailure.NotSent, false)]
    public void OnlyAFileFaultLetsTheBatchGoOn(int failure, bool goesOn) =>
        Assert.Equal(goesOn, TransferOutcome.ContinuesAfter((TransferFailure)failure));
}
