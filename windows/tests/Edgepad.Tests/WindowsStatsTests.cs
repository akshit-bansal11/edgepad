using System.Net;
using System.Net.NetworkInformation;
using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

public sealed class WindowsStatsTests
{
    [Theory]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Ethernet, true)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Wireless80211, true)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Loopback, false)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Tunnel, false)]
    [InlineData(OperationalStatus.Down, NetworkInterfaceType.Ethernet, false)]
    [InlineData(OperationalStatus.Dormant, NetworkInterfaceType.Wireless80211, false)]
    public void OnlyUpRealInterfacesCount(OperationalStatus status, NetworkInterfaceType type, bool counts) =>
        Assert.Equal(counts, WindowsStats.Counts(status, type));

    [Fact]
    public void TheFirstRealIpv4GatewayIsPicked()
    {
        var picked = WindowsStats.PickGateway(
        [
            IPAddress.Parse("fe80::1"),
            IPAddress.Any,
            IPAddress.Loopback,
            IPAddress.Parse("192.168.1.1"),
            IPAddress.Parse("10.0.0.1"),
        ]);

        Assert.Equal(IPAddress.Parse("192.168.1.1"), picked);
    }

    [Fact]
    public void NoGatewayIsNull()
    {
        Assert.Null(WindowsStats.PickGateway([]));
        Assert.Null(WindowsStats.PickGateway([IPAddress.Parse("fe80::1"), IPAddress.Any]));
    }

    [Fact]
    public void DisksAreShapedForTheWire()
    {
        const long gib = 1L << 30;
        var disks = WindowsStats.ShapeDisks(
        [
            ("C:\\", 500 * gib, 125 * gib),
            ("D:\\", 0, 0),
            ("E:\\", 100 * gib, 200 * gib),
            ("F:\\", 1000 * gib, 1000 * gib),
        ]);

        Assert.Equal(2, disks.Count);
        Assert.Equal(new DiskEntry("C", 75, 500, 125), disks[0]);
        Assert.Equal(new DiskEntry("F", 0, 1000, 1000), disks[1]);
    }

    [Fact]
    public void AtMostSixDisksAreKept()
    {
        var drives = Enumerable.Range(0, 9).Select(i => ($"{(char)('C' + i)}:\\", 10L << 30, 5L << 30));

        Assert.Equal(WindowsStats.MaxDisks, WindowsStats.ShapeDisks(drives).Count);
    }

    [Fact]
    public void TheRealMachineReadsInRange()
    {
        var stats = new WindowsStats();
        stats.ReadFast();
        var sample = stats.ReadFast();
        var info = stats.ReadInfo();

        Assert.InRange(sample.Cpu, 0, 100);
        Assert.InRange(sample.Ram, 0, 100);
        Assert.True(sample.Down >= 0);
        Assert.True(sample.Up >= 0);
        Assert.Equal(-1, sample.CpuTemp);
        Assert.True(info.RamMiB > 0);
        Assert.All(stats.ReadDisks(), d => Assert.InRange(d.UsedPct, 0, 100));
    }
}
