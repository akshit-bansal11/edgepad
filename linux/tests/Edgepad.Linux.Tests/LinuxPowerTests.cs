using Edgepad.Platform;
using Edgepad.Power;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class LinuxPowerTests
{
    [Theory]
    [InlineData(PowerCaps.Sleep, "Suspend", "CanSuspend")]
    [InlineData(PowerCaps.Hibernate, "Hibernate", "CanHibernate")]
    [InlineData(PowerCaps.Restart, "Reboot", "CanReboot")]
    [InlineData(PowerCaps.ShutDown, "PowerOff", "CanPowerOff")]
    public void EachPowerActionMapsToItsLogindMethodAndQuery(PowerCaps cap, string method, string query)
    {
        Assert.Equal(method, LinuxPower.MethodFor(cap));
        Assert.Equal(query, LinuxPower.CanFor(cap));
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
}
