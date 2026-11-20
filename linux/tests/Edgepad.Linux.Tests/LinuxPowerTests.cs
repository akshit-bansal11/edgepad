using Edgepad.Media;
using Edgepad.Platform;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class LinuxPowerTests
{
    [Theory]
    [InlineData(0, "Suspend")]
    [InlineData(1, "PowerOff")]
    public void EachKindMapsToItsLogindMethod(int kind, string method) =>
        Assert.Equal(method, LinuxPower.MethodFor((EndKind)kind));

    [Theory]
    [InlineData("yes", true)]
    [InlineData("challenge", true)]
    [InlineData("na", false)]
    [InlineData("no", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyAYesOrAChallengeMeansLogindCan(string? answer, bool allowed) =>
        Assert.Equal(allowed, LinuxPower.Allowed(answer));

    [Fact]
    public void AShutdownWithNoCountdownWindowIsRefused() =>
        Assert.False(LinuxPower.Perform(EndKind.Shutdown));

    [Fact]
    public void TheEndActionTokenIsOfferedOnlyWhenAsked()
    {
        Assert.DoesNotContain("endaction", Annotator.Features(annotate: true));
        Assert.EndsWith("/endaction", Annotator.Features(annotate: true, endAction: true), StringComparison.Ordinal);
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
