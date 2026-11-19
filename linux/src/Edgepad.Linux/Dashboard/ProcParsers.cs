using System.Globalization;
using System.Net;

namespace Edgepad.Dashboard;

/// <summary>
/// Pure readers for the kernel's text files the Linux stats come from (<c>/proc/stat</c>, <c>/proc/meminfo</c>,
/// <c>/proc/net/dev</c>, <c>/proc/net/route</c>, <c>/proc/cpuinfo</c>, sysfs values). None throws: text of any other shape
/// reads as "unknown" (null, -1, or nothing).
/// </summary>
internal static class ProcParsers
{
    private static readonly char[] Blank = [' ', '\t'];

    // Pseudo and in-memory filesystems that are never a disk the owner thinks of as one.
    private static readonly HashSet<string> NotDisks = new(StringComparer.Ordinal)
    {
        "tmpfs", "squashfs", "overlay", "proc", "sysfs", "devtmpfs", "devpts", "cgroup", "cgroup2", "securityfs", "pstore",
        "debugfs", "tracefs", "configfs", "fusectl", "mqueue", "hugetlbfs", "autofs", "binfmt_misc", "bpf", "efivarfs",
        "nsfs", "ramfs", "rpc_pipefs", "selinuxfs", "fuse.portal", "fuse.gvfsd-fuse", "fuse.snapfuse", "nfsd", "iso9660",
    };

    /// <summary>
    /// The first <c>cpu </c> line of <c>/proc/stat</c>: idle (idle + iowait) and total (user through steal; guest time is
    /// already inside user and nice). Null without that line or with fewer than five numbers.
    /// </summary>
    public static (long Idle, long Total)? CpuTicks(string stat)
    {
        foreach (var line in stat.Split('\n'))
        {
            if (!line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                continue;
            }

            var fields = line.Split(Blank, StringSplitOptions.RemoveEmptyEntries);
            var ticks = new List<long>();
            for (var i = 1; i < fields.Length && i <= 8; i++)
            {
                if (!long.TryParse(fields[i], NumberStyles.None, CultureInfo.InvariantCulture, out var t))
                {
                    return null;
                }

                ticks.Add(t);
            }

            if (ticks.Count < 5)
            {
                return null;
            }

            return (ticks[3] + ticks[4], ticks.Sum());
        }

        return null;
    }

    /// <summary>
    /// <c>MemTotal</c> and <c>MemAvailable</c> in KiB from <c>/proc/meminfo</c>. A kernel older than 3.14 has no MemAvailable;
    /// then free + buffers + cached stands in, as <c>free</c> once computed it. Null without MemTotal.
    /// </summary>
    public static (long TotalKiB, long AvailableKiB)? Memory(string meminfo)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in meminfo.Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var rest = line[(colon + 1)..].Split(Blank, StringSplitOptions.RemoveEmptyEntries);
            if (rest.Length >= 1 && long.TryParse(rest[0], NumberStyles.None, CultureInfo.InvariantCulture, out var kib))
            {
                values[line[..colon].Trim()] = kib;
            }
        }

        if (!values.TryGetValue("MemTotal", out var total) || total <= 0)
        {
            return null;
        }

        var available = values.TryGetValue("MemAvailable", out var a)
            ? a
            : values.GetValueOrDefault("MemFree") + values.GetValueOrDefault("Buffers") + values.GetValueOrDefault("Cached");
        return (total, Math.Clamp(available, 0, total));
    }

    /// <summary>True for an interface whose traffic is the laptop's own: not loopback, not a container bridge or veth pair.</summary>
    public static bool IsCounted(string name) =>
        name.Length > 0
        && name != "lo"
        && !name.StartsWith("docker", StringComparison.Ordinal)
        && !name.StartsWith("veth", StringComparison.Ordinal)
        && !name.StartsWith("br-", StringComparison.Ordinal);

    /// <summary>Received and sent bytes summed over the counted interfaces of <c>/proc/net/dev</c>.</summary>
    public static (long Rx, long Tx) NetBytes(string netdev)
    {
        long rx = 0;
        long tx = 0;
        foreach (var line in netdev.Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon].Trim();
            var fields = line[(colon + 1)..].Split(Blank, StringSplitOptions.RemoveEmptyEntries);
            if (!IsCounted(name) || fields.Length < 9
                || !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var r)
                || !long.TryParse(fields[8], NumberStyles.None, CultureInfo.InvariantCulture, out var t))
            {
                continue;
            }

            rx += r;
            tx += t;
        }

        return (rx, tx);
    }

    /// <summary>
    /// The IPv4 default route of <c>/proc/net/route</c> with the lowest metric: its interface and gateway. The kernel prints
    /// the address as the in-memory 32-bit value in hex, so its host-order bytes are the address's network-order bytes.
    /// </summary>
    public static (string Interface, IPAddress Gateway)? DefaultRoute(string route)
    {
        const int RtfUp = 0x1;
        const int RtfGateway = 0x2;
        (string, IPAddress)? best = null;
        var bestMetric = long.MaxValue;
        foreach (var line in route.Split('\n').Skip(1))
        {
            var f = line.Split(Blank, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 7
                || f[1] != "00000000"
                || !uint.TryParse(f[2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var gateway)
                || !int.TryParse(f[3], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var flags)
                || !long.TryParse(f[6], NumberStyles.None, CultureInfo.InvariantCulture, out var metric)
                || (flags & (RtfUp | RtfGateway)) != (RtfUp | RtfGateway)
                || gateway == 0
                || metric >= bestMetric)
            {
                continue;
            }

            bestMetric = metric;
            best = (f[0], new IPAddress(BitConverter.GetBytes(gateway)));
        }

        return best;
    }

    /// <summary>The first <c>model name</c> of <c>/proc/cpuinfo</c> (x86), else its <c>Hardware</c> or <c>Model</c> line (Arm), else empty.</summary>
    public static string CpuName(string cpuinfo)
    {
        string? fallback = null;
        foreach (var line in cpuinfo.Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (value.Length == 0)
            {
                continue;
            }

            if (key == "model name")
            {
                return value;
            }

            if (key is "Hardware" or "Model")
            {
                fallback ??= value;
            }
        }

        return fallback ?? string.Empty;
    }

    /// <summary>A sysfs integer such as <c>temp1_input</c> divided by <paramref name="scale"/> and rounded; -1 when it does not read or is negative.</summary>
    public static int Scaled(string? text, long scale)
    {
        if (text is null || !long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            return -1;
        }

        return (int)Math.Min(int.MaxValue, (long)Math.Round((double)value / scale, MidpointRounding.AwayFromZero));
    }

    /// <summary>Whole percent of <paramref name="total"/>, clamped to 0..100; -1 without a total.</summary>
    public static int Percent(long used, long total) =>
        total <= 0 ? -1 : (int)Math.Clamp(Math.Round(100.0 * used / total, MidpointRounding.AwayFromZero), 0, 100);

    /// <summary>True for a filesystem type that is a real disk (ext4, btrfs, xfs, vfat, ntfs ...), not a pseudo or in-memory one.</summary>
    public static bool IsRealFilesystem(string format) => format.Length > 0 && !NotDisks.Contains(format);
}
