using Edgepad.Media;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class EndActionTests
{
    private readonly List<EndKind> performed = [];
    private readonly List<string> states = [];
    private readonly EndAction end;

    public EndActionTests() =>
        end = new EndAction(
            kind =>
            {
                performed.Add(kind);
                return true;
            },
            states.Add);

    private static MediaSnapshot Playing(string id, int at, int of = 200) => new(id, true, at, of);

    private static MediaSnapshot Stopped(string id, int at, int of = 200) => new(id, false, at, of);

    private static readonly MediaSnapshot Nothing = new("", false, 0, 0);

    [Fact]
    public void ItRefusesToArmWithNoTimeline()
    {
        Assert.False(end.Arm(EndKind.Sleep));
        end.Observe(Playing("live", 10, 0));
        Assert.False(end.Arm(EndKind.Sleep));
        Assert.Empty(states);
    }

    [Fact]
    public void ItFiresWhenPlaybackStopsInTheLastThreeSeconds()
    {
        end.Observe(Playing("a", 50));
        Assert.True(end.Arm(EndKind.Shutdown));
        end.Observe(Stopped("a", 120));
        Assert.Empty(performed);
        end.Observe(Stopped("a", 197));
        Assert.Equal([EndKind.Shutdown], performed);
        Assert.Equal(["shutdown", "none"], states);
    }

    [Fact]
    public void ItFiresWhenTheTrackChangesAfterTheEnd()
    {
        end.Observe(Playing("a", 198));
        Assert.True(end.Arm(EndKind.Sleep));
        end.Observe(Playing("b", 1));
        Assert.Equal([EndKind.Sleep], performed);
    }

    [Fact]
    public void ItDoesNotFireWhenTheTrackChangesEarly()
    {
        end.Observe(Playing("a", 50));
        Assert.True(end.Arm(EndKind.Sleep));
        end.Observe(Playing("b", 1));
        Assert.Empty(performed);
    }

    [Fact]
    public void ItFiresWhenTheSessionVanishesAfterTheEnd()
    {
        end.Observe(Playing("a", 199));
        Assert.True(end.Arm(EndKind.Sleep));
        end.Observe(Nothing);
        Assert.Equal([EndKind.Sleep], performed);
    }

    [Fact]
    public void ItDoesNotFireWhenTheSessionVanishesEarly()
    {
        end.Observe(Playing("a", 20));
        Assert.True(end.Arm(EndKind.Sleep));
        end.Observe(Nothing);
        Assert.Empty(performed);
    }

    [Fact]
    public void CancelDisarmsAndArmingAgainReplacesTheKind()
    {
        end.Observe(Playing("a", 50));
        Assert.True(end.Arm(EndKind.Sleep));
        Assert.True(end.Arm(EndKind.Sleep));
        Assert.True(end.Arm(EndKind.Shutdown));
        end.Cancel();
        end.Cancel();
        Assert.Equal(["sleep", "shutdown", "none"], states);
        end.Observe(Stopped("a", 199));
        Assert.Empty(performed);

        Assert.True(end.Arm(EndKind.Sleep));
        Assert.True(end.Arm(EndKind.Shutdown));
        end.Observe(Stopped("a", 199));
        Assert.Equal([EndKind.Shutdown], performed);
    }

    [Fact]
    public void ItFiresOnce()
    {
        end.Observe(Playing("a", 50));
        Assert.True(end.Arm(EndKind.Sleep));
        end.Observe(Stopped("a", 199));
        end.Observe(Stopped("a", 199));
        end.Observe(Nothing);
        Assert.Single(performed);
    }
}
