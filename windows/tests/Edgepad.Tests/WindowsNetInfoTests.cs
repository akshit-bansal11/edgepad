using System.Net.NetworkInformation;
using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class WindowsNetInfoTests
{
    [Theory]
    [InlineData(NetworkInterfaceType.Wireless80211, NetKind.Wifi)]
    [InlineData(NetworkInterfaceType.Ethernet, NetKind.Ethernet)]
    [InlineData(NetworkInterfaceType.GigabitEthernet, NetKind.Ethernet)]
    [InlineData(NetworkInterfaceType.Wwanpp, NetKind.Cellular)]
    [InlineData(NetworkInterfaceType.Ppp, NetKind.Other)]
    public void AdapterTypeMapsToKind(NetworkInterfaceType type, string kind) => Assert.Equal(kind, WindowsNetInfo.KindOf(type));

    [Theory]
    [InlineData(0L, -1)]
    [InlineData(-1L, -1)]
    [InlineData(866_700_000L, 866)]
    [InlineData(1_000_000_000L, 1000)]
    public void SpeedIsWholeMbps(long bits, int mbps) => Assert.Equal(mbps, WindowsNetInfo.Mbps(bits));
}
