using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>What the Security window's PIN boxes accept: six ASCII digits, typed twice the same.</summary>
public sealed class PinEntryTests
{
    [Theory]
    [InlineData("123456", "123456")]
    [InlineData("12a3-4 56", "123456")]
    [InlineData("1234567890", "123456")]
    [InlineData("١٢٣", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void OnlyTheFirstSixAsciiDigitsSurvive(string? typed, string kept) =>
        Assert.Equal(kept, PinEntry.Digits(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    public void ANonPinIsRefusedWhateverTheSecondBoxSays(string? first)
    {
        Assert.Equal("A PIN is exactly six digits.", PinEntry.Problem(first, first));
    }

    [Fact]
    public void TwoDifferentPinsAreRefused() =>
        Assert.Equal("The two PINs are not the same.", PinEntry.Problem("123456", "123457"));

    [Fact]
    public void TwoEqualPinsPass() =>
        Assert.Null(PinEntry.Problem("123456", "123456"));

    [Fact]
    public void NoMessageRepeatsTheDigits()
    {
        Assert.DoesNotContain("123456", PinEntry.Problem("123456", "654321"), StringComparison.Ordinal);
        Assert.DoesNotContain("12345", PinEntry.Problem("12345", "12345"), StringComparison.Ordinal);
    }
}
