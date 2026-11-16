using Edgepad.Bluetooth;
using Xunit;

namespace Edgepad.Tests;

/// <summary>What a Bluetooth phone is called: its bonded name, cleaned, or its address.</summary>
public sealed class BluetoothLinkTests
{
    [Fact]
    public void ANameWithANewlineIsCleaned()
    {
        Assert.Equal("PixelEvil", BluetoothLink.LabelFor("AA:BB", "Pixel\nEvil"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoNameFallsBackToTheAddress(string? name)
    {
        Assert.Equal("AA:BB", BluetoothLink.LabelFor("AA:BB", name));
    }
}
