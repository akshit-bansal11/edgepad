using System.Net;
using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

public sealed class ProcParsersTests
{
    private const string Stat =
        "cpu  4705 356 584 3699176 23060 0 277 0 0 0\n" +
        "cpu0 1393 280 274 922831 6064 0 245 0 0 0\n" +
        "cpu1 1084 23 117 925694 5643 0 15 0 0 0\n" +
        "cpu2 1107 27 98 925446 5776 0 9 0 0 0\n" +
        "cpu3 1121 26 95 925205 5577 0 8 0 0 0\n" +
        "intr 114930548 113199788 3 0 5 263 0 4 [...]\n" +
        "ctxt 1990473\n" +
        "btime 1062191376\n";

    private const string MemInfo =
        "MemTotal:       16303428 kB\n" +
        "MemFree:         1200000 kB\n" +
        "MemAvailable:    8151714 kB\n" +
        "Buffers:          300000 kB\n" +
        "Cached:          4000000 kB\n" +
        "SwapTotal:       2097148 kB\n";

    private const string OldMemInfo =
        "MemTotal:       1000000 kB\n" +
        "MemFree:         100000 kB\n" +
        "Buffers:          50000 kB\n" +
        "Cached:          250000 kB\n";

    private const string NetDev =
        "Inter-|   Receive                                                |  Transmit\n" +
        " face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed\n" +
        "    lo: 9000000   5000    0    0    0     0          0         0  9000000   5000    0    0    0     0       0          0\n" +
        "wlp2s0: 1000 10 0 0 0 0 0 0 2000 20 0 0 0 0 0 0\n" +
        "  eth0:300 3 0 0 0 0 0 0 400 4 0 0 0 0 0 0\n" +
        "docker0: 77777 1 0 0 0 0 0 0 77777 1 0 0 0 0 0 0\n" +
        "br-1a2b3c: 55555 1 0 0 0 0 0 0 55555 1 0 0 0 0 0 0\n" +
        "veth12ab: 33333 1 0 0 0 0 0 0 33333 1 0 0 0 0 0 0\n";

    private const string Route =
        "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n" +
        "eth0\t00000000\t0102A8C0\t0003\t0\t0\t600\t00000000\t0\t0\t0\n" +
        "wlp2s0\t00000000\t0101A8C0\t0003\t0\t0\t100\t00000000\t0\t0\t0\n" +
        "wlp2s0\t0001A8C0\t00000000\t0001\t0\t0\t100\t00FFFFFF\t0\t0\t0\n";

    [Fact]
    public void CpuTicksReadTheSummaryLineOfAFourCoreStat()
    {
        var ticks = ProcParsers.CpuTicks(Stat);
        Assert.Equal((3699176L + 23060L, 4705L + 356 + 584 + 3699176 + 23060 + 0 + 277 + 0), ticks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cpu0 1 2 3 4 5\n")]
    [InlineData("cpu  1 2 x 4 5\n")]
    [InlineData("cpu  1 2 3\n")]
    public void CpuTicksOfAnotherShapeAreUnknown(string text) => Assert.Null(ProcParsers.CpuTicks(text));

    [Fact]
    public void MemoryUsesMemAvailable() => Assert.Equal((16303428L, 8151714L), ProcParsers.Memory(MemInfo));

    [Fact]
    public void MemoryWithoutMemAvailableAddsFreeBuffersAndCached() => Assert.Equal((1000000L, 400000L), ProcParsers.Memory(OldMemInfo));

    [Fact]
    public void MemoryWithoutTotalIsUnknown() => Assert.Null(ProcParsers.Memory("MemFree: 5 kB\n"));

    [Fact]
    public void NetBytesSkipLoopbackAndContainerInterfaces() => Assert.Equal((1300L, 2400L), ProcParsers.NetBytes(NetDev));

    [Theory]
    [InlineData("lo", false)]
    [InlineData("docker0", false)]
    [InlineData("veth9f", false)]
    [InlineData("br-abc", false)]
    [InlineData("tun0", false)]
    [InlineData("tap1", false)]
    [InlineData("wg0", false)]
    [InlineData("tailscale0", false)]
    [InlineData("virbr0", false)]
    [InlineData("wlan0", true)]
    [InlineData("enp3s0", true)]
    [InlineData("bridge0", true)]
    public void CountedInterfaces(string name, bool counted) => Assert.Equal(counted, ProcParsers.IsCounted(name));

    [Fact]
    public void DefaultRouteHasTheLowestMetric()
    {
        Assert.Equal(("wlp2s0", IPAddress.Parse("192.168.1.1")), ProcParsers.DefaultRoute(Route));
    }

    [Fact]
    public void NoDefaultRouteIsNull() => Assert.Null(ProcParsers.DefaultRoute(Route.Split('\n')[0] + "\n"));

    [Theory]
    [InlineData("processor\t: 0\nmodel name\t: AMD Ryzen 7 5800H with Radeon Graphics\n", "AMD Ryzen 7 5800H with Radeon Graphics")]
    [InlineData("processor\t: 0\nBogoMIPS\t: 108.00\nModel\t: Raspberry Pi 4 Model B Rev 1.4\n", "Raspberry Pi 4 Model B Rev 1.4")]
    [InlineData("", "")]
    public void CpuName(string text, string expected) => Assert.Equal(expected, ProcParsers.CpuName(text));

    [Theory]
    [InlineData("45000\n", 1000, 45)]
    [InlineData("45500", 1000, 46)]
    [InlineData("15250000\n", 1000000, 15)]
    [InlineData("", 1000, -1)]
    [InlineData("hot", 1000, -1)]
    [InlineData("-5000", 1000, -1)]
    [InlineData(null, 1000, -1)]
    public void HwmonValuesAreScaled(string? text, long scale, int expected) => Assert.Equal(expected, ProcParsers.Scaled(text, scale));

    [Theory]
    [InlineData("ext4", true)]
    [InlineData("btrfs", true)]
    [InlineData("vfat", true)]
    [InlineData("tmpfs", false)]
    [InlineData("squashfs", false)]
    [InlineData("overlay", false)]
    [InlineData("proc", false)]
    [InlineData("sysfs", false)]
    [InlineData("devtmpfs", false)]
    [InlineData("nfs4", false)]
    [InlineData("cifs", false)]
    [InlineData("smb3", false)]
    [InlineData("9p", false)]
    [InlineData("fuse.sshfs", false)]
    [InlineData("fuse.rclone", false)]
    [InlineData("fuseblk", true)]
    public void RealFilesystems(string format, bool real) => Assert.Equal(real, ProcParsers.IsRealFilesystem(format));

    [Fact]
    public void MountsAreReadWithTheirEscapesDecoded()
    {
        const string Text = "/dev/nvme0n1p2 / ext4 rw,relatime 0 0\n"
            + "server:/export /mnt/nas nfs4 rw 0 0\n"
            + "/dev/sdb1 /media/My\\040Disk vfat rw 0 0\n"
            + "short line\n";

        Assert.Equal(
            new List<(string, string)> { ("/", "ext4"), ("/mnt/nas", "nfs4"), ("/media/My Disk", "vfat") },
            ProcParsers.Mounts(Text));
    }

    [Fact]
    public void DisksAreShaped()
    {
        const long G = 1L << 30;
        var disks = LinuxStats.Disks(
        [
            ("/", "ext4", 100 * G, 25 * G),
            ("/proc", "proc", 0, 0),
            ("/dev/shm", "tmpfs", 8 * G, 8 * G),
            ("/boot/efi", "vfat", G / 2, G / 4),
            ("/snap/core/1", "squashfs", G, 0),
            ("/home", "btrfs", 100 * G, 25 * G), // the same device as / by its totals
            ("/media/me/USB Stick", "exfat", 64 * G, 60 * G),
        ]);
        Assert.Equal(
            new List<DiskEntry> { new("root", 75, 100, 25), new("USB Stick", 6, 64, 60) },
            disks);
    }

    [Fact]
    public void DisksStopAtSix()
    {
        var mounts = Enumerable.Range(1, 9).Select(i => ($"/mnt/d{i}", "ext4", (long)i << 30, 0L));
        Assert.Equal(6, LinuxStats.Disks(mounts).Count);
    }

    [Fact]
    public void UnreadableFilesReadAsUnknown()
    {
        var root = Directory.CreateTempSubdirectory("edgepad-stats").FullName;
        try
        {
            using var stats = new LinuxStats(root, root);
            stats.ReadFast();
            var sample = stats.ReadFast();
            Assert.Equal(new StatsSample(-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1), sample);
            var info = stats.ReadInfo();
            Assert.Equal(-1, info.RamMiB);
            Assert.Equal(string.Empty, info.NetName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CpuIsReadFromTwoSamples()
    {
        var root = Directory.CreateTempSubdirectory("edgepad-stats").FullName;
        try
        {
            using var stats = new LinuxStats(root, root);
            File.WriteAllText(Path.Combine(root, "stat"), "cpu  100 0 0 900 0 0 0 0\n");
            Assert.Equal(-1, stats.ReadFast().Cpu);
            File.WriteAllText(Path.Combine(root, "stat"), "cpu  150 0 0 950 0 0 0 0\n");
            Assert.Equal(50, stats.ReadFast().Cpu);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
