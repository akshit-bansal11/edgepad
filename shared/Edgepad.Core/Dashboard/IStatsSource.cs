namespace Edgepad.Dashboard;

/// <summary>
/// Where the laptop's numbers come from; one implementation per desktop. Every method may throw (a counter is missing, a driver
/// is busy) and the reporter then publishes unknowns. <see cref="ReadFast"/> leaves Ping and Loss at -1: the reporter fills them.
/// </summary>
internal interface IStatsSource
{
    /// <summary>The once-a-second numbers: CPU, memory, GPU, network rates, temperatures, power.</summary>
    StatsSample ReadFast();

    /// <summary>Names and memory sizes; read at start and rarely after.</summary>
    StatsInfo ReadInfo();

    /// <summary>The mounted disks.</summary>
    IReadOnlyList<DiskEntry> ReadDisks();

    /// <summary>Round trip to the default gateway in milliseconds, or -1 when it did not answer.</summary>
    ValueTask<int> PingAsync(CancellationToken cancel);
}
