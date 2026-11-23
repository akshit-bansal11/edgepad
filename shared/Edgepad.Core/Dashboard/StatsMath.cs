namespace Edgepad.Dashboard;

/// <summary>CPU use from two readings of cumulative idle and total ticks.</summary>
internal static class CpuMeter
{
    /// <summary>Busy share 0..100 between two readings. A counter that went backwards (reset, wrap) or did not move reads as 0.</summary>
    public static int Percent(long prevIdle, long prevTotal, long idle, long total)
    {
        var dTotal = total - prevTotal;
        var dIdle = idle - prevIdle;
        if (dTotal <= 0 || dIdle < 0 || dIdle > dTotal)
        {
            return 0;
        }

        return (int)Math.Round(100.0 * (dTotal - dIdle) / dTotal, MidpointRounding.AwayFromZero);
    }
}

/// <summary>Turns a cumulative byte counter into KiB per second.</summary>
internal sealed class RateMeter
{
    private long? lastBytes;
    private long lastMs;

    /// <summary>The first reading, a counter that went down, and a clock that did not advance all give 0.</summary>
    public int Update(long bytes, long nowMs)
    {
        var rate = 0;
        if (lastBytes is { } before && bytes >= before && nowMs > lastMs)
        {
            rate = (int)Math.Min(int.MaxValue, (bytes - before) * 1000 / ((nowMs - lastMs) * 1024));
        }

        lastBytes = bytes;
        lastMs = nowMs;
        return rate;
    }
}

/// <summary>The share of the last 20 pings that failed.</summary>
internal sealed class LossMeter
{
    public const int Window = 20;

    private readonly bool[] failed = new bool[Window];
    private int next;
    private int count;

    public void Record(bool ok)
    {
        failed[next] = !ok;
        next = (next + 1) % Window;
        count = Math.Min(count + 1, Window);
    }

    /// <summary>0..100, or -1 before any ping.</summary>
    public int Percent => count == 0 ? -1 : (int)Math.Round(100.0 * failed.Take(count).Count(f => f) / count, MidpointRounding.AwayFromZero);
}
