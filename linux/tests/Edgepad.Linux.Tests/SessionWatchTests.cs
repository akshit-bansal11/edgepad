using Edgepad.Platform;
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
}
