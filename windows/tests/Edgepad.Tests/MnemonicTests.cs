using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class MnemonicTests
{
    [Theory]
    [InlineData("&Wi-Fi and USB connections", "Wi-Fi and USB connections", 'W')]
    [InlineData("Share clipboard", "Share clipboard", null)]
    [InlineData("Allow through Windows &Firewall…", "Allow through Windows Firewall…", 'F')]
    [InlineData("Tom && &Jerry", "Tom & Jerry", 'J')]
    [InlineData("Fish &&", "Fish &", null)]
    [InlineData("s&tart", "start", 'T')]
    [InlineData("", "", null)]
    public void LabelsSplitIntoNameAndKey(string label, string plain, char? key)
    {
        Assert.Equal(plain, Mnemonic.Plain(label));
        Assert.Equal(key, Mnemonic.Key(label));
    }

    [Fact]
    public void EscapedTextHasNoKeyAndReadsBack()
    {
        var escaped = Mnemonic.Escape("DNS & TLS failed");
        Assert.Null(Mnemonic.Key(escaped));
        Assert.Equal("DNS & TLS failed", Mnemonic.Plain(escaped));
    }
}
