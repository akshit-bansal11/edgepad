using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

public sealed class StatusTextTests
{
    [Fact]
    public void AnOlderPhoneIsToldToUpdateThePhone() =>
        Assert.Equal(
            "Pixel could not connect: its Edgepad app is older. Update Edgepad on the phone.",
            StatusText.Mismatch("Pixel", 4, 5));

    [Fact]
    public void AnOlderLaptopIsToldToUpdateTheLaptop() =>
        Assert.Equal(
            "Pixel could not connect: Edgepad on this laptop is older. Update Edgepad here.",
            StatusText.Mismatch("Pixel", 6, 5));

    [Theory]
    [InlineData("Refused a phone it does not trust", true)]
    [InlineData("Bluetooth is off", true)]
    [InlineData("Bluetooth is off or unavailable; trying again", true)]
    [InlineData("Pixel could not connect: its Edgepad app is older. Update Edgepad on the phone.", true)]
    [InlineData("Waiting for your phone", false)]
    [InlineData("Connected to Pixel · Bluetooth", false)]
    public void OnlyRefusalsAndBrokenRadiosAreProblems(string status, bool problem) =>
        Assert.Equal(problem, StatusText.IsProblem(status));

    [Fact]
    public void ConnectedNamesThePhoneAndTheWay() =>
        Assert.Equal("Connected to Pixel · Wi-Fi", StatusText.Connected("Pixel", "Wi-Fi"));
}
