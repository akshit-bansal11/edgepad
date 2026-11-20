using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Edgepad.Dashboard;

/// <summary>
/// This Linux laptop's numbers for TEXT STATS, STATS_INFO and DISKS, read from <c>/proc</c> and <c>/sys</c> as any user may.
/// Every number the machine does not give is -1, never 0. One caller at a time (the reporter's timer) uses <see cref="ReadFast"/>,
/// which keeps the previous CPU and byte counters; nothing is read until a method is called.
/// </summary>
internal sealed class LinuxStats(string proc = "/proc", string sys = "/sys") : IStatsSource, IDisposable
{
    private const int MaxDisks = 6;
    private const long Gibibyte = 1L << 30;
    private static readonly TimeSpan PingTimeout = TimeSpan.FromMilliseconds(1000);

    // CPU temperature drivers: Intel, AMD, and the out-of-tree Zen one.
    private static readonly string[] CpuSensors = ["coretemp", "k10temp", "zenpower"];

    // Mounts that are never a disk the owner thinks of as one, whatever their filesystem.
    private static readonly string[] Hidden = ["/boot", "/snap", "/run", "/proc", "/sys", "/dev", "/var/lib/docker"];

    private readonly LinuxGpu gpu = new(Path.Combine(sys, "class", "drm"));
    private readonly RateMeter down = new();
    private readonly RateMeter up = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Lazy<string?> cpuSensor = new(() => FindCpuSensor(Path.Combine(sys, "class", "hwmon")));
    private (long Idle, long Total)? lastCpu;

    public StatsSample ReadFast()
    {
        var cpu = -1;
        if (ProcParsers.CpuTicks(Read("stat") ?? string.Empty) is { } ticks)
        {
            if (lastCpu is { } before)
            {
                cpu = CpuMeter.Percent(before.Idle, before.Total, ticks.Idle, ticks.Total);
            }

            lastCpu = ticks;
        }

        var ram = ProcParsers.Memory(Read("meminfo") ?? string.Empty) is { } mem
            ? ProcParsers.Percent(mem.TotalKiB - mem.AvailableKiB, mem.TotalKiB)
            : -1;

        var downRate = -1;
        var upRate = -1;
        if (Read("net/dev") is { } netdev)
        {
            var (rx, tx) = ProcParsers.NetBytes(netdev);
            var now = clock.ElapsedMilliseconds;
            downRate = down.Update(rx, now);
            upRate = up.Update(tx, now);
        }

        var g = gpu.Read();
        var cpuTemp = cpuSensor.Value is { } sensor ? ProcParsers.Scaled(ReadFile(Path.Combine(sensor, "temp1_input")), 1000) : -1;
        return new StatsSample(cpu, ram, g.Util, g.Vram, downRate, upRate, -1, -1, cpuTemp, g.Temp, g.PowerW);
    }

    public StatsInfo ReadInfo()
    {
        var ramMiB = ProcParsers.Memory(Read("meminfo") ?? string.Empty) is { } mem ? (int)Math.Min(int.MaxValue, mem.TotalKiB / 1024) : -1;
        var (gpuName, vramMiB) = gpu.Info();
        var net = ProcParsers.DefaultRoute(Read("net/route") ?? string.Empty)?.Interface ?? string.Empty;
        return new StatsInfo(ProcParsers.CpuName(Read("cpuinfo") ?? string.Empty), ramMiB, gpuName, vramMiB, net);
    }

    public IReadOnlyList<DiskEntry> ReadDisks()
    {
        // From the mount table, filtered BEFORE any size query: a network or FUSE mount whose server is gone blocks statvfs.
        var mounts = new List<(string Mount, string Format, long Total, long Free)>();
        foreach (var (mount, format) in ProcParsers.Mounts(Read("self/mounts") ?? string.Empty))
        {
            if (!ProcParsers.IsRealFilesystem(format) || IsHidden(mount))
            {
                continue;
            }

            try
            {
                var drive = new DriveInfo(mount);
                mounts.Add((mount, format, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A mount that went away or is not ours to read is simply not listed.
            }
        }

        return Disks(mounts);
    }

    public async ValueTask<int> PingAsync(CancellationToken cancel)
    {
        if (ProcParsers.DefaultRoute(Read("net/route") ?? string.Empty) is not { } route)
        {
            return -1;
        }

        try
        {
            // .NET sends ICMP itself where the kernel allows unprivileged ping sockets and runs ping(8) where it does not.
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(route.Gateway, PingTimeout, null, null, cancel).ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? (int)Math.Min(int.MaxValue, reply.RoundtripTime) : -1;
        }
        catch (Exception e) when (e is PingException or InvalidOperationException)
        {
            return -1;
        }
    }

    /// <summary>The phone stopped watching: NVML is let go until the next reading.</summary>
    public void Idle() => gpu.Idle();

    public void Dispose() => gpu.Dispose();

    /// <summary>
    /// The disks worth showing, in mount order: real filesystems only, not the boot, snap or runtime mounts, each device once
    /// (btrfs subvolumes and bind mounts repeat the same totals), at most six. <c>/</c> is labelled <c>root</c>, others by their last folder.
    /// </summary>
    internal static List<DiskEntry> Disks(IEnumerable<(string Mount, string Format, long Total, long Free)> mounts)
    {
        var seen = new HashSet<(long, long)>();
        var disks = new List<DiskEntry>();
        foreach (var (mount, format, total, free) in mounts)
        {
            if (disks.Count == MaxDisks)
            {
                break;
            }

            var path = mount.Length > 1 ? mount.TrimEnd('/') : mount;
            if (total <= 0
                || !ProcParsers.IsRealFilesystem(format)
                || IsHidden(path)
                || !seen.Add((total, free)))
            {
                continue;
            }

            var label = path == "/" ? "root" : Path.GetFileName(path);
            var used = Math.Clamp(total - free, 0, total);
            disks.Add(new DiskEntry(label, ProcParsers.Percent(used, total), total / Gibibyte, Math.Max(0, free) / Gibibyte));
        }

        return disks;
    }

    private static bool IsHidden(string mount)
    {
        var path = mount.Length > 1 ? mount.TrimEnd('/') : mount;
        return Hidden.Any(h => path == h || path.StartsWith(h + "/", StringComparison.Ordinal));
    }

    private static string? FindCpuSensor(string hwmon)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(hwmon).Order(StringComparer.Ordinal))
            {
                if (ReadFile(Path.Combine(dir, "name"))?.Trim() is { } name && CpuSensors.Contains(name))
                {
                    return dir;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // No hwmon folder: no temperature.
        }

        return null;
    }

    private static string? ReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? Read(string file) => ReadFile(Path.Combine(proc, file));
}
