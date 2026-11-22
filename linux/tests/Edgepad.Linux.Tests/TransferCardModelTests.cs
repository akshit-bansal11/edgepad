using Edgepad.Platform;
using Edgepad.Transfer;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>What the Linux transfer card says, and how a receive batch reaches the feed it reads.</summary>
public sealed class TransferCardModelTests
{
    private static TransferView Running(bool sending, int index, int total, long speed = 0, bool paused = false) =>
        new(TransferPhase.Running, sending, index, total, "a.txt", 10, 100, speed, 0, [], 0, false, paused);

    [Fact]
    public void TheTitleNamesTheDirectionAndThePhone()
    {
        Assert.Equal("Sending to Pixel", TransferCardText.Title(Running(true, 1, 1), "Pixel"));
        Assert.Equal("Receiving from Pixel", TransferCardText.Title(Running(false, 1, 0), "Pixel"));
    }

    [Fact]
    public void ADetailLineShowsTheCountOnlyForMoreThanOneFileAndTheSpeedOnlyOnceKnown()
    {
        Assert.Equal("a.txt", TransferCardText.Detail(Running(true, 1, 1)));
        Assert.Equal("a.txt · 2 of 5", TransferCardText.Detail(Running(true, 2, 5)));
        Assert.StartsWith("a.txt · 2 of 5 · ", TransferCardText.Detail(Running(true, 2, 5, speed: 2048)));
        Assert.EndsWith("/s", TransferCardText.Detail(Running(true, 2, 5, speed: 2048)));
    }

    [Fact]
    public void AReceiveWithNoKnownTotalCountsWhatHasArrived() =>
        Assert.Equal("a.txt · 3 of 3", TransferCardText.Detail(Running(false, 3, 0)));

    [Fact]
    public void AHeldFileSaysPausedWithNoCountOrSpeed() =>
        Assert.Equal("a.txt · Paused", TransferCardText.Detail(Running(true, 2, 5, speed: 2048, paused: true)));

    [Fact]
    public void AFailureNamesTheReasonInTheDirectionsWords()
    {
        var failed = new TransferView(TransferPhase.Finished, true, 1, 1, "a.txt", 10, 100, 0, 0, [new TransferFailed("a.txt", TransferFailure.Refused)], 0, false);
        Assert.Equal("Not sent: the phone could not take it", TransferCardText.Result(failed));
        Assert.Equal("Not received: this laptop could not take it", TransferCardText.Result(failed with { Sending = false }));
    }

    [Fact]
    public void TheBarIsFullOnlyWhenTheBatchEndedWithNoFailure()
    {
        Assert.Equal(0.1, TransferCardText.Fraction(Running(true, 1, 1)));
        var done = Running(true, 1, 1) with { Phase = TransferPhase.Finished };
        Assert.Equal(1, TransferCardText.Fraction(done));
        Assert.Equal(0.1, TransferCardText.Fraction(done with { Failed = [new TransferFailed("a.txt", TransferFailure.Cancelled)] }));
    }

    [Fact]
    public void AReceiveBecomesOneBatchOfTheFilesThatArrived()
    {
        var feed = new TransferFeed();
        var batch = new ReceiveBatch(feed);
        batch.Progress("a.txt", 10, 100);
        batch.Arrived("a.txt");
        batch.Progress("b.txt", 5, 50);

        var view = feed.View;
        Assert.Equal(TransferPhase.Running, view.Phase);
        Assert.False(view.Sending);
        Assert.Equal(2, view.Index);
        Assert.Equal(1, view.Done);
        Assert.Equal("b.txt", view.Name);
    }

    [Fact]
    public void AReceiveNeverTouchesASendThatIsShowing()
    {
        var feed = new TransferFeed();
        feed.BeginBatch(sending: true, 2);
        new ReceiveBatch(feed).Progress("a.txt", 10, 100);

        Assert.True(feed.View.Sending);
        Assert.Equal(0, feed.View.Index);
    }

    [Fact]
    public void AFailedReceiveIsListedWithItsReason()
    {
        var feed = new TransferFeed();
        new ReceiveBatch(feed).Failed("a.txt", XferCode.BadHash);

        Assert.Equal(TransferFailure.Damaged, Assert.Single(feed.View.Failed).Why);
    }
}
