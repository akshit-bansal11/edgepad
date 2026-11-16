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

    private static void Cancel(PairingLimiter limiter, string host)
    {
        Assert.True(limiter.TryBegin(host));
        limiter.End(PairingOutcome.Cancelled, host);
    }

    private static void TimeOut(PairingLimiter limiter, string host)
    {
        Assert.True(limiter.TryBegin(host));
        limiter.End(PairingOutcome.TimedOut, host);
    }

    [Fact]
    public void ACancelRefusesThatHostForAMinuteAndNoOtherHost()
    {
        var limiter = NewLimiter();
        Cancel(limiter, "a");

        Assert.True(limiter.TryBegin("b"));
        limiter.End(PairingOutcome.Abandoned, "b");

        time = 59_999;
        Assert.False(limiter.TryBegin("a"));
        Assert.True(limiter.TryBegin("b"));
        limiter.End(PairingOutcome.Abandoned, "b");

        time = 60_000;
        Assert.True(limiter.TryBegin("a"));
    }

    [Fact]
    public void TheRefusalDoublesWithEachFurtherCancelAndStopsAtAnHour()
    {
        var limiter = NewLimiter();
        Cancel(limiter, "a");
        foreach (var seconds in new long[] { 60, 120, 240, 480, 960, 1920, 3600, 3600 })
        {
            var until = time + (seconds * 1000);
            time = until - 1;
            Assert.False(limiter.TryBegin("a"));
            time = until;
            TimeOut(limiter, "a");
        }
    }

    [Fact]
    public void APairingClearsTheHostsStreak()
    {
        var limiter = NewLimiter();
        Cancel(limiter, "a");
        time = 60_000;
        Cancel(limiter, "a");

        time += 120_000;
        Assert.True(limiter.TryBegin("a"));
        limiter.End(PairingOutcome.Paired, "a");
        Cancel(limiter, "a");

        // Back to a minute, not four.
        time += 59_999;
        Assert.False(limiter.TryBegin("a"));
        time += 1;
        Assert.True(limiter.TryBegin("a"));
    }

    [Fact]
    public void CancelsAndTimeoutsNeverCountTowardTheFiveFailureLock()
    {
        // Nine, one under the hourly cap, from nine hosts, then four failures: a fifth attempt is still allowed, so
        // none of the nine counted as a failure.
        var limiter = NewLimiter();
        for (var i = 0; i < 9; i++)
        {
            if (i % 2 == 0)
            {
                Cancel(limiter, $"host{i}");
            }
            else
            {
                TimeOut(limiter, $"host{i}");
            }
        }

        Fail(limiter, 4);
        Assert.True(limiter.TryBegin());
    }

    [Fact]
    public void TheHourlyCapRefusesEveryoneUntilTheOldestFallsOut()
    {
        var limiter = NewLimiter();
        for (var i = 0; i < 10; i++)
        {
            time = i * 1000;
            Cancel(limiter, $"host{i}");
        }

        time = 30_000;
        Assert.False(limiter.TryBegin("new"));
        Assert.False(limiter.TryBegin());

        time = 3_600_000;
        Assert.True(limiter.TryBegin("new"));
    }

    [Fact]
    public void TheHostTableStaysBounded()
    {
        // End without TryBegin: the hourly cap would otherwise stop this at ten, and only the table is under test.
        var limiter = NewLimiter();
        for (var i = 0; i < 100; i++)
        {
            time = i;
            limiter.End(PairingOutcome.Cancelled, $"host{i}");
        }

        Assert.True(limiter.TrackedHosts <= 64);
    }
}
