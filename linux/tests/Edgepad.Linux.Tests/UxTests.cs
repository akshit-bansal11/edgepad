using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class UxTests
{
    [Fact]
    public void AMissingUinputNodeIsAProblemWithAReason()
    {
        var problem = UinputAccess.Problem("/nonexistent/uinput");
        Assert.NotNull(problem);
        Assert.Contains("/nonexistent/uinput", problem);
    }

    [Fact]
    public void ANodeThatOpensIsNoProblem() => Assert.Null(UinputAccess.Problem("/dev/null"));

    [Fact]
    public void TheInputStateWordsAreTheWireOnes()
    {
        Assert.Equal("blocked", UinputAccess.StateText(true));
        Assert.Equal("ok", UinputAccess.StateText(false));
    }

    [Fact]
    public void TheAutostartEntryNamesTheRunningFile()
    {
        var entry = Autostart.Entry("/opt/edgepad/edgepad");
        Assert.Contains("Exec=/opt/edgepad/edgepad\n", entry);
        Assert.Contains("X-GNOME-Autostart-enabled=true", entry);
        Assert.StartsWith("[Desktop Entry]\n", entry);
    }

    [Theory]
    [InlineData("/usr/local/bin/edgepad", "/usr/local/bin/edgepad")]
    [InlineData("/home/a b/edgepad", "\"/home/a b/edgepad\"")]
    [InlineData("/home/$USER/edgepad", "edgepad")]
    public void TheExecValueIsQuotedOrFallsBack(string path, string expected) => Assert.Equal(expected, Autostart.ExecField(path));

    [Fact]
    public void AutostartIsWrittenAndRemoved()
    {
        var folder = Path.Combine(Path.GetTempPath(), "edgepad-autostart-" + Guid.NewGuid().ToString("N"));
        try
        {
            var autostart = new Autostart(folder, "/usr/bin/edgepad");
            Assert.False(autostart.Enabled);
            Assert.True(autostart.Set(true));
            Assert.True(autostart.Enabled);
            Assert.True(autostart.Set(false));
            Assert.False(autostart.Enabled);
            Assert.True(autostart.Set(false));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ARunningCopyIsReadBack() =>
        Assert.Equal(("4.0.1", 1234), SingleInstance.Parse(SingleInstance.Line("4.0.1", 1234)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4.0.1")]
    [InlineData("4.0.1 abc")]
    [InlineData("4.0.1 1")]
    [InlineData("4.0.1 -5")]
    public void AnUnreadableRunningFileNamesNobody(string? text) => Assert.Null(SingleInstance.Parse(text));

    [Fact]
    public void TheSameVersionOnlySaysSo() =>
        Assert.Equal(SecondLaunch.Notice, SingleInstance.Decide(("4.0.1", 99), "4.0.1", sameProgram: true));

    [Fact]
    public void ADifferentVersionTakesOver() =>
        Assert.Equal(SecondLaunch.TakeOver, SingleInstance.Decide(("4.0.0", 99), "4.0.1", sameProgram: true));

    [Fact]
    public void AnUnknownCopyOrProcessIsNeverAskedToQuit()
    {
        Assert.Equal(SecondLaunch.Notice, SingleInstance.Decide(null, "4.0.1", sameProgram: true));
        Assert.Equal(SecondLaunch.Notice, SingleInstance.Decide(("4.0.0", 99), "4.0.1", sameProgram: false));
    }

    [Theory]
    [InlineData("/usr/local/bin/edgepad", "/home/a/edgepad", true)]
    [InlineData("/usr/local/bin/edgepad (deleted)", "/usr/local/bin/edgepad", true)]
    [InlineData("/usr/bin/firefox", "/usr/local/bin/edgepad", false)]
    [InlineData(null, "/usr/local/bin/edgepad", false)]
    public void TheProcessIsRecognisedByFileName(string? exe, string mine, bool expected) =>
        Assert.Equal(expected, SingleInstance.SameProgram(exe, mine));

    [Fact]
    public void AnOlderPhoneIsToldToUpdateThePhone() =>
        Assert.Equal(
            "Pixel could not connect: its Edgepad app is older. Update Edgepad on the phone.",
            StatusText.Rewrite("Refused a phone on protocol 4; this laptop is on 5", "AA:BB", "Pixel", "Pixel · Bluetooth"));

    [Fact]
    public void AnOlderLaptopIsToldToUpdateHere() =>
        Assert.Equal(
            "Pixel could not connect: Edgepad on this laptop is older. Update Edgepad here.",
            StatusText.Rewrite("Refused a phone on protocol 6; this laptop is on 5", "AA:BB", "Pixel", "Pixel · Bluetooth"));

    [Fact]
    public void ConnectedNamesThePhoneAndTheWayIn() =>
        Assert.Equal(
            "Connected to Pixel · Bluetooth",
            StatusText.Rewrite("Connected to AA:BB:CC:DD:EE:FF", "AA:BB:CC:DD:EE:FF", "Pixel", "Pixel · Bluetooth"));

    [Theory]
    [InlineData("AA-BB-CC-DD-EE-FF", true)]
    [InlineData("aa:bb:cc:dd:ee:ff", true)]
    [InlineData("Pixel 8", false)]
    public void AnAliasThatIsTheAddressIsNoName(string alias, bool isAddress) =>
        Assert.Equal(isAddress, PhoneName.IsAddress(alias, "AA:BB:CC:DD:EE:FF"));
}
