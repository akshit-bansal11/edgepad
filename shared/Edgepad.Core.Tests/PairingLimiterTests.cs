using Edgepad.Trust;
using Xunit;

namespace Edgepad.Tests;

public sealed class PairingLimiterTests
{
    // Milliseconds on the limiter's clock, moved by hand.
    private long time;

    private PairingLimiter NewLimiter() => new(() => time);

    private static void Fail(PairingLimiter limiter, int times)
    {
        for (var i = 0; i < times; i++)
        {
            Assert.True(limiter.TryBegin());
            limiter.End(PairingOutcome.Failed);
        }
    }

    [Fact]
    public void OnePairingAtATime()
    {
        var limiter = NewLimiter();

        Assert.True(limiter.TryBegin());
        Assert.False(limiter.TryBegin());
        limiter.End(PairingOutcome.Paired);
        Assert.True(limiter.TryBegin());
    }

    [Fact]
    public void FiveFailuresLockForFiveMinutesThenTenAfterFiveMore()
    {
        var limiter = NewLimiter();
        Fail(limiter, 4);
        Assert.True(limiter.TryBegin());
        limiter.End(PairingOutcome.Failed);
        Assert.False(limiter.TryBegin());

        time += 299_999;
        Assert.False(limiter.TryBegin());
        time += 1;
        Fail(limiter, 5);

        time += 599_999;
        Assert.False(limiter.TryBegin());
        time += 1;
        Assert.True(limiter.TryBegin());
    }

    [Fact]
    public void TheLockNeverPassesAnHour()
    {
        var limiter = NewLimiter();
        for (var round = 0; round < 10; round++)
        {
            Fail(limiter, 5);
            time += 3_600_000;
        }

        Fail(limiter, 5);
        time += 3_600_000;
        Assert.True(limiter.TryBegin());
    }

    [Fact]
    public void TheOwnerCancellingDoesNotCount()
    {
        var limiter = NewLimiter();
        for (var i = 0; i < 20; i++)
        {
            Assert.True(limiter.TryBegin());
            limiter.End(PairingOutcome.Abandoned);
        }

        Fail(limiter, 4);
        Assert.True(limiter.TryBegin());
    }

    [Fact]
    public void APairingClearsTheCount()
    {
        var limiter = NewLimiter();
        Fail(limiter, 4);
        Assert.True(limiter.TryBegin());
        limiter.End(PairingOutcome.Paired);

        Fail(limiter, 4);
        Assert.True(limiter.TryBegin());
    }
}
