using Edgepad.Dispatch;
using Xunit;

namespace Edgepad.Tests;

public class CaptureOverlayTests
{
    private long now;
    private readonly CaptureOverlay overlay;

    public CaptureOverlayTests() => overlay = new CaptureOverlay(() => now);

    [Fact]
    public void NothingPendingMeansNoEscape()
    {
        Assert.False(overlay.Begin(opensOverlay: true));
        Assert.False(new CaptureOverlay(() => now).Cancel());
    }

    [Fact]
    public void ANewActionWhileAnOverlayIsOpenDismissesItFirst()
    {
        overlay.Begin(opensOverlay: true);
        now += 1_000;

        Assert.True(overlay.Begin(opensOverlay: false));
        Assert.False(overlay.IsPending);
    }

    [Fact]
    public void AnActionThatOpensNothingLeavesNothingPending()
    {
        overlay.Begin(opensOverlay: false);

        Assert.False(overlay.Cancel());
    }

    [Fact]
    public void CancelDismissesOnceThenIsQuiet()
    {
        overlay.Begin(opensOverlay: true);

        Assert.True(overlay.Cancel());
        Assert.False(overlay.Cancel());
    }

    [Fact]
    public void TheOverlayIsForgottenAfterTheCeiling()
    {
        overlay.Begin(opensOverlay: true);
        now += CaptureOverlay.PendingMs - 1;
        Assert.True(overlay.IsPending);

        now += 1;
        Assert.False(overlay.IsPending);
        Assert.False(overlay.Cancel());
    }

    [Fact]
    public void ARepeatedOpeningRestartsTheClock()
    {
        overlay.Begin(opensOverlay: true);
        now += CaptureOverlay.PendingMs - 1;

        Assert.True(overlay.Begin(opensOverlay: true));
        now += CaptureOverlay.PendingMs - 1;

        Assert.True(overlay.IsPending);
    }
}
