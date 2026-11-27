using Edgepad.Platform;
using Edgepad.Power;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class LinuxPowerTests
{
    [Theory]
    [InlineData((int)PowerCaps.Sleep, "Suspend", "CanSuspend")]
    [InlineData((int)PowerCaps.Hibernate, "Hibernate", "CanHibernate")]
    [InlineData((int)PowerCaps.Restart, "Reboot", "CanReboot")]
    [InlineData((int)PowerCaps.ShutDown, "PowerOff", "CanPowerOff")]
    public void EachPowerActionMapsToItsLogindMethodAndQuery(int cap, string method, string query)
    {
        // An int, because a public test method cannot take the internal PowerCaps (CS0051).
        Assert.Equal(method, LinuxPower.MethodFor((PowerCaps)cap));
        Assert.Equal(query, LinuxPower.CanFor((PowerCaps)cap));
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("challenge", false)]
    [InlineData("na", false)]
    [InlineData("no", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyAnExactYesMeansLogindCan(string? answer, bool allowed) =>
        Assert.Equal(allowed, LinuxPower.Allowed(answer));

    [Fact]
    public void ARestartWithNoCountdownWindowIsRefused() =>
        Assert.False(LinuxPower.Shared.StartCountdown(PowerKind.Restart, 60, _ => { }));

    [Fact]
    public void TheCurrentSessionHearsTheStateAndAnEarlierOneEndingLeavesIt()
    {
        List<string> first = [];
        List<string> second = [];
        Action<string> a = first.Add;
        Action<string> b = second.Add;
        LinuxPower.Listen(a);
        LinuxPower.Listen(b);
        LinuxPower.Unlisten(a);
        LinuxPower.Report("");
        Assert.Empty(first);
        Assert.Equal([""], second);

        LinuxPower.Unlisten(b);
        LinuxPower.Report("restart/9");
        Assert.Equal([""], second);
    }

    [Fact]
    public void EachCapabilityBecomesItsFeatureToken()
    {
        Assert.DoesNotContain("sleep", Annotator.Features(annotate: true));
        Assert.EndsWith(
            "/sleep/hibernate/restart/shutdown",
            Annotator.Features(annotate: true, power: PowerCaps.Sleep | PowerCaps.Hibernate | PowerCaps.Restart | PowerCaps.ShutDown),
            StringComparison.Ordinal);
        Assert.Equal("capture/sleep/shutdown", Annotator.Features(annotate: false, power: PowerCaps.Sleep | PowerCaps.ShutDown));
    }
}

public sealed class ShutdownCountdownTests
{
    [Theory]
    [InlineData(1_000L, 1_000L, 0)]
    [InlineData(1_000L, 2_000L, 0)]
    [InlineData(61_000L, 1_000L, 60)]
    [InlineData(61_000L, 1_001L, 60)]
    [InlineData(61_000L, 60_001L, 1)]
    public void TheSecondsLeftRoundUpAndStopAtZero(long deadline, long now, int expected) =>
        Assert.Equal(expected, ShutdownCountdown.SecondsLeft(deadline, now));

    [Theory]
    [InlineData(250L, 250L, false)]
    [InlineData(250L, 4_000L, false)]
    [InlineData(250L, 30_000L, true)]
    public void AWallClockJumpPastTheMonotonicOneIsASuspend(long mono, long wall, bool slept) =>
        Assert.Equal(slept, ShutdownCountdown.Slept(mono, wall));
}
