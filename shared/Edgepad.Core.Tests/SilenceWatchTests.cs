using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

public sealed class SilenceWatchTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    [Fact]
    public void NothingFiresWhileFramesKeepArriving()
    {
        long now = 0;
        var fired = 0;
        using var watch = new SilenceWatch(Limit, () => fired++, () => now);
        for (var i = 0; i < 10; i++)
        {
            now += 20_000;
            watch.Touch();
            Assert.False(watch.Check());
        }

        Assert.Equal(0, fired);
    }

    [Fact]
    public void FiresOnceAfterTheLimit()
    {
        long now = 0;
        var fired = 0;
        using var watch = new SilenceWatch(Limit, () => fired++, () => now);
        now = 30_000;
        Assert.False(watch.Check());
        now = 30_001;
        Assert.True(watch.Check());
        Assert.False(watch.Check());
        Assert.Equal(1, fired);
    }

    [Fact]
    public void DisposeStopsTheTimer()
    {
        var fired = 0;
        var watch = new SilenceWatch(TimeSpan.Zero, () => fired++, () => 1, TimeSpan.FromMilliseconds(20));
        watch.Dispose();
        Thread.Sleep(150);
        Assert.Equal(0, fired);
    }
}
