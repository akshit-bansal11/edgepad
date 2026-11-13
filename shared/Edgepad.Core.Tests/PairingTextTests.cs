using System.Net;
using System.Net.NetworkInformation;
using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

public sealed class PairingTextTests
{
    [Fact]
    public void TheLineNamesTheAddressAndTheWay() =>
        Assert.Equal("from 192.168.1.23, over Wi-Fi", PairingText.Describe(IPAddress.Parse("192.168.1.23"), "over Wi-Fi"));

    [Fact]
    public void AnUnknownWayIsYourNetwork() =>
        Assert.Equal("from 10.0.0.5, on your network", PairingText.Describe(IPAddress.Parse("10.0.0.5"), null));

    [Theory]
    [InlineData("Ethernet 3", "Remote NDIS based Internet Sharing Device", NetworkInterfaceType.Ethernet)]
    [InlineData("usb0", "usb0", NetworkInterfaceType.Ethernet)]
    [InlineData("rndis0", "rndis0", NetworkInterfaceType.Ethernet)]
    [InlineData("enx1a2b", "UsbNcm Host Device (NCM)", NetworkInterfaceType.Ethernet)]
    public void ATetherAdapterIsUsb(string name, string description, NetworkInterfaceType type) =>
        Assert.Equal("over USB", PairingText.WayOf(name, description, type));

    [Fact]
    public void AWirelessAdapterIsWifi() =>
        Assert.Equal("over Wi-Fi", PairingText.WayOf("Wi-Fi", "Intel(R) Wi-Fi 7 BE200", NetworkInterfaceType.Wireless80211));

    [Fact]
    public void AWiredAdapterIsUnnamed() =>
        Assert.Null(PairingText.WayOf("Ethernet", "Realtek PCIe GbE Family Controller", NetworkInterfaceType.Ethernet));

    [Fact]
    public void NoAddressIsNoSource() => Assert.Null(PairingText.SourceOf(null));

    [Fact]
    public void ThePairingWindowSaysWhatItAlwaysHas()
    {
        Assert.Equal("That pairing code did not match. Nothing was paired.", PairingText.Mismatched);
        Assert.Equal("Time ran out. Nothing was paired. Start again on the phone.", PairingText.TimedOut);
        Assert.Equal("Only pair a phone that is yours. If you did not just start pairing, choose Cancel.", PairingText.Warning);
        Assert.Equal("Pairing did not finish.", PairingText.NotFinished);
        Assert.Equal("Pairing code matched. Tap Yes on Pixel to finish.", PairingText.Matched("Pixel"));
        Assert.Equal("Paired with Pixel.", PairingText.Paired("Pixel"));
    }
}
