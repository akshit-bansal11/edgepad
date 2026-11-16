using Edgepad.Injection;
using Xunit;

namespace Edgepad.Tests;

/// <summary>When the phone is told Windows is refusing input, and when it is told that stopped.</summary>
public sealed class BlockedTrackerTests
{
    private long now = 1000;
    private readonly List<bool> said = [];

    private BlockedTracker Tracker() => new(() => now, said.Add);

    [Fact]
    public void TheFirstRefusalSaysBlockedOnceAndRepeatsSayNothing()
    {
        var tracker = Tracker();

        tracker.Refused();
        now += 10;
        tracker.Refused();
        tracker.Refused();

        Assert.Equal([true], said);
    }

    [Fact]
    public void InputTakenWithinTwoSecondsOfARefusalSaysNothing()
    {
        var tracker = Tracker();
        tracker.Refused();

        now += 1999;
        tracker.Accepted();

        Assert.Equal([true], said);
    }

    [Fact]
    public void InputTakenTwoSecondsAfterTheLastRefusalSaysOkOnce()
    {
        var tracker = Tracker();
        tracker.Refused();

        now += 2000;
        tracker.Accepted();
        tracker.Accepted();

        Assert.Equal([true, false], said);
    }

    [Fact]
    public void InputTakenWhenNeverBlockedSaysNothing()
    {
        var tracker = Tracker();

        tracker.Accepted();

        Assert.Empty(said);
    }
}
