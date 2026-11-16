using Edgepad.Dispatch;
using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

/// <summary>Which session statuses reach the tray, and which link a Bluetooth restart waits for.</summary>
public sealed class SessionHostStatusTests
{
    private static readonly LaptopStatus Refused = LaptopStatus.RefusedProtocol(4, 5);

    [Fact]
    public void AStatusIsShownWhenNoPhoneIsConnected()
    {
        Assert.True(SessionHost.ShowStatus(null, "AA:BB", Refused));
    }

    [Fact]
    public void AnotherPhonesStatusIsDroppedWhileOneIsConnected()
    {
        Assert.False(SessionHost.ShowStatus("192.168.1.20", "AA:BB", Refused));
    }

    [Fact]
    public void TheConnectedPhonesOwnStatusIsShown()
    {
        Assert.True(SessionHost.ShowStatus("aa:bb", "AA:BB", Refused));
    }

    [Fact]
    public void AConnectedStatusIsAlwaysShown()
    {
        Assert.True(SessionHost.ShowStatus("192.168.1.20", "AA:BB", LaptopStatus.Connected("Pixel", PhoneWay.Bluetooth)));
    }

    [Fact]
    public void ARestartWaitsOnlyForABluetoothPhone()
    {
        Assert.True(SessionHost.ShouldDefer(PhoneWay.Bluetooth));
        Assert.False(SessionHost.ShouldDefer(PhoneWay.WifiOrUsb));
        Assert.False(SessionHost.ShouldDefer(null));
    }
}
