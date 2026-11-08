using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

public sealed class LaptopStatusTests
{
    [Fact]
    public void AnOlderPhoneIsToldToUpdateThePhone() =>
        Assert.Equal(
            "Pixel could not connect: its Edgepad app is older. Update Edgepad on the phone.",
            LaptopStatus.Mismatch("Pixel", 4, 5));

    [Fact]
    public void AnOlderLaptopIsToldToUpdateTheLaptop() =>
        Assert.Equal(
            "Pixel could not connect: Edgepad on this laptop is older. Update Edgepad here.",
            LaptopStatus.Mismatch("Pixel", 6, 5));

    [Fact]
    public void TheHandshakesProtocolLineBecomesTheOwnersSentence()
    {
        Assert.Equal(
            "Pixel could not connect: its Edgepad app is older. Update Edgepad on the phone.",
            LaptopStatus.Rewrite("Refused a phone on protocol 4; this laptop is on 5", "Pixel"));
        Assert.Equal("Waiting for your phone", LaptopStatus.Rewrite("Waiting for your phone", "Pixel"));
    }

    [Theory]
    [InlineData("Refused a phone it does not trust", true)]
    [InlineData("Bluetooth is off", true)]
    [InlineData("Bluetooth is off or unavailable; trying again", true)]
    [InlineData("Pixel could not connect: its Edgepad app is older. Update Edgepad on the phone.", true)]
    [InlineData("Waiting for your phone", false)]
    [InlineData("Connected to Pixel · Bluetooth", false)]
    public void OnlyRefusalsAndBrokenRadiosAreProblems(string status, bool problem) =>
        Assert.Equal(problem, LaptopStatus.IsProblem(status));

    [Fact]
    public void ConnectedNamesThePhoneAndTheWay() =>
        Assert.Equal("Connected to Pixel · Wi-Fi", LaptopStatus.Connected("Pixel", "Wi-Fi"));
}
