using Edgepad.Startup;
using Xunit;

namespace Edgepad.Tests;

public sealed class SingleInstanceTests
{
    [Theory]
    [InlineData(true, false, (int)LaunchChoice.Run)]
    [InlineData(true, true, (int)LaunchChoice.Run)]
    [InlineData(false, true, (int)LaunchChoice.Notify)]
    [InlineData(false, false, (int)LaunchChoice.TakeOver)]
    public void OnlyADifferentVersionTakesOver(bool lockFree, bool sameVersion, int expected) =>
        Assert.Equal((LaunchChoice)expected, SingleInstance.Decide(lockFree, sameVersion));

    [Fact]
    public void EachVersionHasItsOwnName() =>
        Assert.NotEqual(SingleInstance.VersionMutexName("4.0.0"), SingleInstance.VersionMutexName("4.0.1"));
}
