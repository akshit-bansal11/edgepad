using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The phone's own name, cleaned before the laptop shows or logs it; the cases of the Mac's PhoneLabelTests.</summary>
public sealed class PhoneLabelTests
{
    [Theory]
    [InlineData("Akshit's Pixel", "Akshit's Pixel")]
    [InlineData("Pixel\n\r\u0007Phone", "PixelPhone")]
    [InlineData("Pixel‮⁦moc.exe", "Pixelmoc.exe")]
    [InlineData("My \"Pixel\"", "My Pixel")]
    [InlineData("  My    Pixel  ", "My Pixel")]
    [InlineData("\"!!\"\n\U0001F600", "")]
    [InlineData(null, "")]
    public void OnlyLettersDigitsSpacesAndALittlePunctuationAreKept(string? name, string shown)
    {
        Assert.Equal(shown, PhoneLabel.Clean(name));
    }

    [Fact]
    public void CombiningMarksAreRemoved()
    {
        Assert.Equal("e", PhoneLabel.Clean("e" + new string('́', 200)));
    }

    [Fact]
    public void ALongNameIsCutToTheMaximumLength()
    {
        Assert.Equal(new string('a', PhoneLabel.MaxNameLength), PhoneLabel.Clean(new string('a', PhoneLabel.MaxNameLength + 50)));
        Assert.True(PhoneLabel.Clean(string.Concat(Enumerable.Repeat("ab ", 340))).Length <= PhoneLabel.MaxNameLength);
    }

    [Fact]
    public void NothingUsableIsShownAsAPhone()
    {
        Assert.Equal(PhoneLabel.Unnamed, PhoneLabel.Shown("\n‮"));
        Assert.Equal("Pixel", PhoneLabel.Shown("Pixel"));
    }
}
