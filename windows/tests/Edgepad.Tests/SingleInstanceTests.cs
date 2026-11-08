using Edgepad.Startup;
using Xunit;

namespace Edgepad.Tests;

public sealed class SingleInstanceTests
{
    [Theory]
    [InlineData(true, false, LaunchChoice.Run)]
    [InlineData(true, true, LaunchChoice.Run)]
    [InlineData(false, true, LaunchChoice.Notify)]
    [InlineData(false, false, LaunchChoice.TakeOver)]
    public void OnlyADifferentVersionTakesOver(bool lockFree, bool sameVersion, LaunchChoice expected) =>
        Assert.Equal(expected, SingleInstance.Decide(lockFree, sameVersion));

    [Fact]
    public void EachVersionHasItsOwnName() =>
        Assert.NotEqual(SingleInstance.VersionMutexName("4.0.0"), SingleInstance.VersionMutexName("4.0.1"));
}
