using Edgepad.Dispatch;
using Xunit;

namespace Edgepad.Tests;

public sealed class ShutdownCountdownTests
{
    private long now = 1_000;

    [Fact]
    public void FiresOnceWhenTheTimeIsUp()
    {
        var clock = new CountdownClock(60, () => now);
        Assert.Equal(60, clock.SecondsLeft);

        now += 59_000;
        Assert.False(clock.Poll());
        Assert.Equal(1, clock.SecondsLeft);

        now += 1_000;
        Assert.True(clock.Poll());
        Assert.False(clock.Poll());
        Assert.Equal(0, clock.SecondsLeft);
    }

    [Fact]
    public void ACancelledCountdownNeverFires()
    {
        var clock = new CountdownClock(60, () => now);
        now += 30_000;
        clock.Cancel();

        now += 120_000;
        Assert.False(clock.Poll());
    }
}
