using Edgepad.Platform;
using Tmds.DBus.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class SessionWatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASessionInTheBackgroundKeepsPhonesOut(bool holding) => Assert.True(SessionWatch.Holds(holding, active: false));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASessionInFrontLetsPhonesIn(bool holding) => Assert.False(SessionWatch.Holds(holding, active: true));

    // No logind to ask: usable on a desktop without one, but never reopened by a read that failed.
    [Fact]
    public void NoAnswerAtTheStartLetsPhonesIn() => Assert.False(SessionWatch.Holds(holding: false, active: null));

    [Fact]
    public void NoAnswerAfterASwitchAwayKeepsPhonesOut() => Assert.True(SessionWatch.Holds(holding: true, active: null));

    [Fact]
    public void TheSessionStateSaysWhetherItIsInFront()
    {
        Assert.True(SessionWatch.ActiveOf(new Dictionary<string, VariantValue> { ["State"] = VariantValue.String("active") }));
        Assert.False(SessionWatch.ActiveOf(new Dictionary<string, VariantValue> { ["State"] = VariantValue.String("online") }));
        Assert.Null(SessionWatch.ActiveOf(new Dictionary<string, VariantValue> { ["Active"] = VariantValue.Bool(true) }));
        Assert.Null(SessionWatch.ActiveOf(null));
    }

    [Fact]
    public void ThePollIsOnlyASafetyNetWhileTheSignalIsHeard()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), SessionWatch.WaitFor(listening: true));
        Assert.Equal(TimeSpan.FromMilliseconds(500), SessionWatch.WaitFor(listening: false));
    }
}
