using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

public sealed class LaptopStatusTests
{
    [Fact]
    public void AnOlderPhoneIsToldToUpdateThePhone()
    {
        var status = LaptopStatus.RefusedProtocol(4, 5).For("Pixel", PhoneWay.Bluetooth);

        Assert.Equal(StatusKind.Mismatch, status.Kind);
        Assert.Equal("Pixel could not connect: its Edgepad app is older. Update Edgepad on the phone.", status.Text);
    }

    [Fact]
    public void AnOlderLaptopIsToldToUpdateTheLaptop()
    {
        var status = LaptopStatus.RefusedProtocol(6, 5).For("Pixel", PhoneWay.Bluetooth);

        Assert.Equal(StatusKind.Mismatch, status.Kind);
        Assert.Equal("Pixel could not connect: Edgepad on this laptop is older. Update Edgepad here.", status.Text);
    }

    [Fact]
    public void TheHandshakesProtocolReportKeepsItsNumbersUntilAPhoneIsNamed() =>
        Assert.Equal("Refused a phone on protocol 4; this laptop is on 5", LaptopStatus.RefusedProtocol(4, 5).Text);

    [Fact]
    public void NamingAPhoneLeavesAnyOtherStatusAsItIs()
    {
        Assert.Same(LaptopStatus.Waiting, LaptopStatus.Waiting.For("Pixel", PhoneWay.Bluetooth));
        Assert.Equal(StatusKind.RefusedProtocol, LaptopStatus.RefusedProtocol(5, 5).For("Pixel", PhoneWay.Bluetooth).Kind);
    }

    [Fact]
    public void NamingAConnectedPhoneReplacesTheAddressAndTheWay()
    {
        var status = LaptopStatus.Connected("AA:BB:CC:DD:EE:FF", PhoneWay.Bluetooth).For("Pixel", PhoneWay.WifiOrUsb);

        Assert.Equal("Connected to Pixel · Wi-Fi or USB", status.Text);
        Assert.Equal("Pixel · Wi-Fi or USB", status.PhoneLine);
    }

    [Fact]
    public void OnlyRefusalsAndBrokenRadiosAreProblems()
    {
        LaptopStatus[] problems =
        [
            LaptopStatus.RefusedUntrusted,
            LaptopStatus.RefusedPin,
            LaptopStatus.RefusedLockedOut,
            LaptopStatus.RefusedProtocol(4, 5),
            LaptopStatus.RefusedProtocol(4, 5).For("Pixel", PhoneWay.Bluetooth),
            LaptopStatus.BluetoothOff,
            LaptopStatus.BluetoothRetrying,
            LaptopStatus.BluetoothUnavailable,
        ];
        LaptopStatus[] others =
        [
            LaptopStatus.Starting,
            LaptopStatus.Waiting,
            LaptopStatus.Connected("Pixel", PhoneWay.Bluetooth),
            LaptopStatus.Pairing("Pixel"),
            LaptopStatus.Paired("Pixel"),
            LaptopStatus.Asking("Pixel"),
            LaptopStatus.QuietUntil("12:41"),
        ];

        Assert.All(problems, status => Assert.True(status.IsProblem));
        Assert.All(others, status => Assert.False(status.IsProblem));
    }

    [Fact]
    public void OnlyAMismatchIsAnnounced()
    {
        Assert.True(LaptopStatus.RefusedProtocol(4, 5).For("Pixel", PhoneWay.Bluetooth).IsMismatch);
        Assert.False(LaptopStatus.RefusedUntrusted.IsMismatch);
        Assert.False(LaptopStatus.BluetoothOff.IsMismatch);
    }

    [Fact]
    public void OnlyAConnectedStatusIsConnected()
    {
        Assert.True(LaptopStatus.Connected("Pixel", PhoneWay.Bluetooth).IsConnected);
        Assert.False(LaptopStatus.Waiting.IsConnected);
    }

    [Fact]
    public void EveryStatusReadsAsItAlwaysHas()
    {
        Assert.Equal("Starting…", LaptopStatus.Starting.Text);
        Assert.Equal("Waiting for your phone", LaptopStatus.Waiting.Text);
        Assert.Equal("Connected to Pixel · Bluetooth", LaptopStatus.Connected("Pixel", PhoneWay.Bluetooth).Text);
        Assert.Equal("Refused a phone locked out after wrong PINs", LaptopStatus.RefusedLockedOut.Text);
        Assert.Equal("Refused a phone it does not trust", LaptopStatus.RefusedUntrusted.Text);
        Assert.Equal("Refused a phone that did not give the right PIN", LaptopStatus.RefusedPin.Text);
        Assert.Equal("Bluetooth is off", LaptopStatus.BluetoothOff.Text);
        Assert.Equal("Bluetooth is off or unavailable; trying again", LaptopStatus.BluetoothRetrying.Text);
        Assert.Equal("Bluetooth is off or unavailable", LaptopStatus.BluetoothUnavailable.Text);
        Assert.Equal("Pair Pixel: type the pairing code shown on the phone", LaptopStatus.Pairing("Pixel").Text);
        Assert.Equal("Paired with Pixel", LaptopStatus.Paired("Pixel").Text);
        Assert.Equal("Asking whether to trust Pixel", LaptopStatus.Asking("Pixel").Text);
        Assert.Equal("Phones can connect again at 12:41", LaptopStatus.QuietUntil("12:41").Text);
    }
}
