using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class StateReporterTests
{
    [Theory]
    [InlineData(0, 500)]
    [InlineData(9_999, 500)]
    [InlineData(10_000, 3_000)]
    [InlineData(600_000, 3_000)]
    public void TheLevelsAreReadSlowlyOnceThePhoneIsIdle(long idleMs, int expectedMs) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), StateReporter.IntervalFor(idleMs));
}
