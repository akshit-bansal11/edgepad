using Edgepad.Startup;
using Xunit;

namespace Edgepad.Tests;

public sealed class RestartTests
{
    [Theory]
    [InlineData(true, false, false, (int)LaunchChoice.Run)]
    [InlineData(true, true, true, (int)LaunchChoice.Run)]
    [InlineData(false, true, false, (int)LaunchChoice.Notify)]
    [InlineData(false, true, true, (int)LaunchChoice.TakeOver)]
    [InlineData(false, false, true, (int)LaunchChoice.TakeOver)]
    public void RestartTakesOverTheSameVersion(bool lockFree, bool sameVersion, bool restart, int expected) =>
        Assert.Equal((LaunchChoice)expected, SingleInstance.Decide(lockFree, sameVersion, restart));

    [Theory]
    [InlineData(null, "S-1-5-21-1", true)]
    [InlineData(null, null, true)]
    [InlineData("S-1-5-21-1", "S-1-5-21-1", true)]
    [InlineData("s-1-5-21-1", "S-1-5-21-1", true)]
    [InlineData("S-1-5-21-2", "S-1-5-21-1", false)]
    [InlineData("S-1-5-21-1", null, false)]
    public void ARestartIsOnlyForTheSameUser(string? requested, string? current, bool allowed) =>
        Assert.Equal(allowed, SingleInstance.RestartAllowed(requested, current));

    [Fact]
    public void ReadsRestartAndProbe() =>
        Assert.Equal(new LaunchArgs("S-1-5-21-1", true), SingleInstance.Parse(["--probe-notifications", "--restart", "S-1-5-21-1"]));

    [Theory]
    [InlineData("--restart")]
    [InlineData("--restart", "--probe-notifications")]
    [InlineData("--unknown", "x")]
    [InlineData("S-1-5-21-1")]
    public void AnythingElseIsIgnored(params string[] args)
    {
        var parsed = SingleInstance.Parse(args);

        Assert.Null(parsed.RestartSid);
    }

    [Fact]
    public void RestartArgumentsRoundTrip() =>
        Assert.Equal("S-1-5-21-1", SingleInstance.Parse(SingleInstance.RestartArguments("S-1-5-21-1").Split(' ')).RestartSid);
}
