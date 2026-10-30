using Edgepad.Net;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class LinkIdentityTests
{
    [Fact]
    public void DeviceTextIsIdOsAndName()
    {
        var identity = new LinkIdentity([.. Enumerable.Range(0x80, 16).Select(i => (byte)i)], "My/Laptop");

        Assert.Equal("808182838485868788898a8b8c8d8e8f/linux/My/Laptop", identity.DeviceText(DeviceOs.Linux));
    }
}
