using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class LinuxNetInfoTests
{
    [Theory]
    [InlineData("wlp3s0", true, "1\n", NetKind.Wifi)]
    [InlineData("enp2s0", false, "1\n", NetKind.Ethernet)]
    [InlineData("wwan0", false, "1\n", NetKind.Cellular)]
    [InlineData("tun0", false, "65534\n", NetKind.Other)]
    [InlineData("eth0", false, null, NetKind.Other)]
    public void KindFromSysfs(string iface, bool wireless, string? type, string expected) =>
        Assert.Equal(expected, LinuxNetInfo.Kind(iface, wireless, type));

    [Theory]
    [InlineData("1000\n", 1000)]
    [InlineData("2500", 2500)]
    [InlineData("-1\n", -1)]
    [InlineData("0", -1)]
    [InlineData("", -1)]
    [InlineData(null, -1)]
    public void SpeedFile(string? text, int expected) => Assert.Equal(expected, LinuxNetInfo.Mbps(text));

    [Theory]
    [InlineData(2412u, "2.4")]
    [InlineData(2484u, "2.4")]
    [InlineData(5180u, "5")]
    [InlineData(5825u, "5")]
    [InlineData(5955u, "6")]
    [InlineData(7115u, "6")]
    [InlineData(0u, "?")]
    [InlineData(60000u, "?")]
    public void BandFromFrequency(uint mhz, string expected) => Assert.Equal(expected, LinuxNetInfo.Band(mhz));

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(64, 64)]
    [InlineData(250, 100)]
    public void SignalIsAPercent(int strength, int expected) => Assert.Equal(expected, LinuxNetInfo.Signal(strength));
}
