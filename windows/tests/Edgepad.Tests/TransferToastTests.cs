using Edgepad.Transfer;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The toast's words and sizes. The window needs a desktop, so painting is not covered.</summary>
public sealed class TransferToastTests
{
    private static TransferView Running(bool sending = true, int index = 2, int total = 5, bool cancelling = false, bool paused = false) =>
        new(TransferPhase.Running, sending, index, total, "a.pdf", 37, 100, 4096, 1, [], 0, cancelling, paused);

    private static TransferView Finished(int done, params TransferFailed[] failed) =>
        new(TransferPhase.Finished, true, done + failed.Length, done + failed.Length, "a.pdf", 0, 0, 0, done, failed, 0, false);

    [Fact]
    public void RunningTitleNamesTheDirection()
    {
        Assert.Equal(Strings.Xfer_PanelSending.Fill("Pixel"), ToastText.Title(Running(), "Pixel"));
        Assert.Equal(Strings.Xfer_PanelReceiving.Fill("Pixel"), ToastText.Title(Running(sending: false), "Pixel"));
        Assert.Equal(Strings.Xfer_Cancelling, ToastText.Title(Running(cancelling: true), "Pixel"));
    }

    [Fact]
    public void RunningSubtitleCountsOnlyWhenThereIsMoreThanOneFile()
    {
        Assert.StartsWith(Strings.Xfer_PanelDetail.Fill("a.pdf", 2, 5, ByteSize.Format(4096)), ToastText.Subtitle(Running()));
        Assert.Equal(Strings.Xfer_PanelSpeed.Fill("a.pdf", ByteSize.Format(4096)), ToastText.Subtitle(Running(total: 1, index: 1)));
        Assert.Equal(Strings.Xfer_PanelCount.Fill("a.pdf", 2, 5), ToastText.Subtitle(Running() with { BytesPerSecond = 0 }));
        Assert.Equal("a.pdf", ToastText.Subtitle(Running(total: 0, index: 1) with { BytesPerSecond = 0 }));
    }

    [Fact]
    public void AHeldBatchSaysSoInPlaceOfTheSpeed()
    {
        Assert.Equal(Strings.Xfer_PanelHeld.Fill("a.pdf"), ToastText.Subtitle(Running(paused: true)));
    }

    [Fact]
    public void AFailureSaysNotSentOrNotReceivedAndWhy()
    {
        var lost = new TransferFailed("b.pdf", TransferFailure.LinkLost);
        Assert.Equal(Strings.Xfer_NotSent.Fill(ToastText.Reason(TransferFailure.LinkLost)), ToastText.Subtitle(Finished(0, lost)));
        Assert.Equal(
            Strings.Xfer_NotReceived.Fill(ToastText.Reason(TransferFailure.LinkLost)),
            ToastText.Subtitle(Finished(0, lost) with { Sending = false }));
    }

    // Pause and Stop only while a batch runs and is not already stopping, for sends and receives alike; Close always.
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public void ButtonsFollowThePhase(bool sending, bool paused, bool cancelling, bool actions)
    {
        var offered = ToastButtons.Of(Running(sending, paused: paused, cancelling: cancelling));
        Assert.Equal(new ToastButtons(actions, actions, true), offered);
    }

    [Fact]
    public void AFinishedBatchOffersOnlyClose()
    {
        Assert.Equal(new ToastButtons(false, false, true), ToastButtons.Of(Finished(2)));
        Assert.Equal(new ToastButtons(false, false, true), ToastButtons.Of(Finished(0, new TransferFailed("b.pdf", TransferFailure.LinkLost))));
        Assert.Equal(new ToastButtons(false, false, false), ToastButtons.Of(TransferView.Idle));
    }

    [Fact]
    public void FinishedTitlesCoverEveryOutcome()
    {
        var lost = new TransferFailed("b.pdf", TransferFailure.LinkLost);
        Assert.Equal(Strings.Xfer_SentOne.Fill("a.pdf"), ToastText.Title(Finished(1), "Pixel"));
        Assert.Equal(Strings.Xfer_SentMany.Fill(5), ToastText.Title(Finished(5), "Pixel"));
        Assert.Equal(Strings.Xfer_SentSome.Fill(3, 5), ToastText.Title(Finished(3, lost, lost), "Pixel"));
        Assert.Equal(Strings.Xfer_FailedOne.Fill("b.pdf"), ToastText.Title(Finished(0, lost), "Pixel"));
        Assert.Equal(Strings.Xfer_FailedMany.Fill(2), ToastText.Title(Finished(0, lost, lost), "Pixel"));
    }

    [Fact]
    public void APartialReceiveUsesTheReceiveWords()
    {
        var lost = new TransferFailed("b.pdf", TransferFailure.LinkLost);
        var received = Finished(3, lost, lost) with { Sending = false };

        Assert.Equal(Strings.Xfer_ReceivedSome.Fill(3, 5), ToastText.Title(received, "Pixel"));
        Assert.NotEqual(Strings.Xfer_SentSome.Fill(3, 5), ToastText.Title(received, "Pixel"));
    }

    [Theory]
    [InlineData((int)TransferFailure.Refused)]
    [InlineData((int)TransferFailure.Damaged)]
    [InlineData((int)TransferFailure.WriteFailed)]
    [InlineData((int)TransferFailure.Busy)]
    [InlineData((int)TransferFailure.TimedOut)]
    [InlineData((int)TransferFailure.LinkLost)]
    [InlineData((int)TransferFailure.Unreadable)]
    [InlineData((int)TransferFailure.Cancelled)]
    [InlineData((int)TransferFailure.NotSent)]
    public void EveryFailureHasItsOwnWords(int failure)
    {
        var why = (TransferFailure)failure;
        Assert.False(string.IsNullOrWhiteSpace(ToastText.Reason(why)));
        Assert.Equal(Strings.Xfer_NotSent.Fill(ToastText.Reason(why)), ToastText.Subtitle(Finished(0, new TransferFailed("b.pdf", why))));
    }

    [Fact]
    public void ReasonsAreDistinct()
    {
        var all = Enum.GetValues<TransferFailure>().Select(ToastText.Reason).ToArray();
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    [Fact]
    public void PercentIsClampedAndSafeForAnEmptySize()
    {
        Assert.Equal(37, ToastText.Percent(Running()));
        Assert.Equal(0, ToastText.Percent(Running() with { Size = 0 }));
        Assert.Equal(100, ToastText.Percent(Running() with { Sent = 500 }));
    }

    [Fact]
    public void SizingConstantsAreSet()
    {
        Assert.True(TransferToast.CardWidth > 0);
        Assert.True(TransferToast.RunningHeight > 0);
        Assert.True(TransferToast.DoneHeight > 0);
        Assert.True(TransferToast.FailedHeight > TransferToast.RunningHeight);
    }
}
