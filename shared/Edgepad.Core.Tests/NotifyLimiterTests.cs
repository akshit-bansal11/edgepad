using Edgepad.Notify;
using Xunit;

namespace Edgepad.Tests;

public sealed class NotifyLimiterTests
{
    // Milliseconds on the limiter's clock, moved by hand.
    private long time;

    private NotifyLimiter NewLimiter() => new(() => time);

    [Fact]
    public void TenAMinuteThenRefused()
    {
        var limiter = NewLimiter();

        for (var i = 0; i < NotifyLimiter.MaxPerMinute; i++)
        {
            Assert.True(limiter.TryAccept("app", $"t{i}", "b"));
        }

        Assert.False(limiter.TryAccept("app", "late", "b"));
        time = 59_999;
        Assert.False(limiter.TryAccept("app", "later", "b"));
        time = 60_000;
        Assert.True(limiter.TryAccept("app", "after", "b"));
    }

    [Fact]
    public void IdenticalWithinTenSecondsIsDropped()
    {
        var limiter = NewLimiter();

        Assert.True(limiter.TryAccept("app", "t", "b"));
        time = 9_999;
        Assert.False(limiter.TryAccept("app", "t", "b"));
        time = 10_000;
        Assert.True(limiter.TryAccept("app", "t", "b"));
    }

    [Fact]
    public void DifferentFieldsAreNotDuplicates()
    {
        var limiter = NewLimiter();

        Assert.True(limiter.TryAccept("app", "t", "b"));
        Assert.True(limiter.TryAccept("app", "t", "c"));
        Assert.True(limiter.TryAccept("other", "t", "b"));
    }
}
