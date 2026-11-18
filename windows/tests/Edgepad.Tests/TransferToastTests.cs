using System.Globalization;
using Edgepad.Transfer;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The toast's words and sizes. The window needs a desktop, so painting is not covered.</summary>
public sealed class TransferToastTests
{
    private static TransferView Running(bool sending = true, int index = 2, int total = 5, bool cancelling = false) =>
        new(TransferPhase.Running, sending, index, total, "a.pdf", 37, 100, 4096, 1, [], 0, cancelling);

    private static TransferView Finished(int done, params TransferFailed[] failed) =>
        new(TransferPhase.Finished, true, done + failed.Length, done + failed.Length, "a.pdf", 0, 0, 0, done, failed, 0, false);

    [Fact]
    public void RunningTitleNamesTheDirection()
    {
        Assert.Equal(Strings.Xfer_Sending, ToastText.Title(Running()));
        Assert.Equal(Strings.Xfer_Receiving, ToastText.Title(Running(sending: false)));
        Assert.Equal(Strings.Xfer_Cancelling, ToastText.Title(Running(cancelling: true)));
    }

    [Fact]
    public void RunningSubtitleCountsOnlyWhenTheTotalIsKnown()
    {
        Assert.StartsWith(string.Format(CultureInfo.CurrentCulture, Strings.Xfer_Count, 2, 5), ToastText.Subtitle(Running()));
        Assert.Equal("a.pdf", ToastText.Subtitle(Running(sending: false, total: 0)));
    }

    [Fact]
    public void FinishedTitlesCoverEveryOutcome()
    {
        var lost = new TransferFailed("b.pdf", TransferFailure.LinkLost);
        Assert.Equal(Strings.Xfer_SentOne.Fill("a.pdf"), ToastText.Title(Finished(1)));
        Assert.Equal(Strings.Xfer_SentMany.Fill(5), ToastText.Title(Finished(5)));
        Assert.Equal(Strings.Xfer_SentSome.Fill(3, 5), ToastText.Title(Finished(3, lost, lost)));
        Assert.Equal(Strings.Xfer_FailedOne.Fill("b.pdf"), ToastText.Title(Finished(0, lost)));
        Assert.Equal(Strings.Xfer_FailedMany.Fill(2), ToastText.Title(Finished(0, lost, lost)));
    }

    [Theory]
    [InlineData(TransferFailure.Refused)]
    [InlineData(TransferFailure.Damaged)]
    [InlineData(TransferFailure.WriteFailed)]
    [InlineData(TransferFailure.Busy)]
    [InlineData(TransferFailure.TimedOut)]
    [InlineData(TransferFailure.LinkLost)]
    [InlineData(TransferFailure.Unreadable)]
    [InlineData(TransferFailure.Cancelled)]
    [InlineData(TransferFailure.NotSent)]
    public void EveryFailureHasItsOwnWords(TransferFailure why)
    {
        Assert.False(string.IsNullOrWhiteSpace(ToastText.Reason(why)));
        Assert.Equal(ToastText.Reason(why), ToastText.Subtitle(Finished(0, new TransferFailed("b.pdf", why))));
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
    public void DetailLeavesOutSpeedUntilThereIsOne()
    {
        Assert.Contains(ToastText.Bytes(4096), ToastText.Detail(Running()));
        Assert.DoesNotContain(ToastText.Bytes(4096), ToastText.Detail(Running() with { BytesPerSecond = 0 }));
    }

    [Theory]
    [InlineData(0UL, "0 B")]
    [InlineData(1023UL, "1023 B")]
    [InlineData(1024UL, "1.0 KB")]
    [InlineData(1572864UL, "1.5 MB")]
    [InlineData(2147483648UL, "2.0 GB")]
    public void BytesUseBinaryUnits(ulong bytes, string expected)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(expected, ToastText.Bytes(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
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
