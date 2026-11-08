using System.Net;
using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

public sealed class PairingTextTests
{
    [Fact]
    public void ALanAddressIsNamedAndCalledWifi() =>
        Assert.Equal("from 192.168.1.23, over Wi-Fi", PairingText.SourceOf(IPAddress.Parse("192.168.1.23")));

    [Fact]
    public void AMappedAddressIsShownAsIpv4() =>
        Assert.Equal("from 10.0.0.5, over Wi-Fi", PairingText.SourceOf(IPAddress.Parse("::ffff:10.0.0.5")));

    [Fact]
    public void TheLoopbackIsUsb()
    {
        Assert.Equal("from 127.0.0.1, over USB", PairingText.SourceOf(IPAddress.Loopback));
        Assert.Equal("from ::1, over USB", PairingText.SourceOf(IPAddress.IPv6Loopback));
    }

    [Fact]
    public void NoAddressIsNoSource() => Assert.Null(PairingText.SourceOf(null));
}
