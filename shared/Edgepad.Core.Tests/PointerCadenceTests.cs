using System.Diagnostics;
using Edgepad.Dispatch;
using Xunit;

namespace Edgepad.Tests;

public sealed class PointerCadenceTests
{
    private static long Ms(long ms) => Stopwatch.Frequency * ms / 1000;

    // The same 50 frames, each gapMs apart, so only the gap decides which bucket they land in.
    private static string Steady(long gap)
    {
        var cadence = new PointerCadence();
        for (var i = 0; i < 50; i++)
        {
            cadence.Record(1000 + (i * gap));
        }

        return cadence.Summary() ?? "";
    }

    [Fact]
    public void FewerThanFiftyFramesSayNothing()
    {
        var cadence = new PointerCadence();
        for (var i = 0; i < 49; i++)
        {
            cadence.Record(1000 + (i * Ms(10)));
        }

        Assert.Null(cadence.Summary());
    }

    [Fact]
    public void ABucketEdgeBelongsToTheUpperBucket()
    {
        Assert.Contains("gaps under 5 ms: 49, 5-20: 0", Steady(Ms(5) - 1), StringComparison.Ordinal);
        Assert.Contains("gaps under 5 ms: 0, 5-20: 49", Steady(Ms(5)), StringComparison.Ordinal);
        Assert.Contains("20-50: 49, 50-100: 0", Steady(Ms(20)), StringComparison.Ordinal);
        Assert.Contains("50-100: 49, over 100: 0", Steady(Ms(50)), StringComparison.Ordinal);
        Assert.Contains("50-100: 0, over 100: 49", Steady(Ms(100)), StringComparison.Ordinal);
    }

    [Fact]
    public void AGapOverTwoHundredFiftyMsEndsTheRunAndIsNotCounted()
    {
        var cadence = new PointerCadence();
        var t = 1000L;
        for (var i = 0; i < 25; i++)
        {
            cadence.Record(t);
            t += Ms(10);
        }

        t += Ms(251);
        for (var i = 0; i < 25; i++)
        {
            cadence.Record(t);
            t += Ms(10);
        }

        var summary = cadence.Summary();

        // 48 gaps inside the two runs; the pause between them is in no bucket and not in the longest.
        Assert.Contains("gaps under 5 ms: 0, 5-20: 48, 20-50: 0, 50-100: 0, over 100: 0; longest 10 ms", summary, StringComparison.Ordinal);
        Assert.StartsWith("50 in ", summary, StringComparison.Ordinal);
    }
}
