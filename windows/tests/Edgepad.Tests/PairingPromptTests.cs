using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// What the pairing window keeps of what the owner types, and its countdown. The window itself needs a desktop, so
/// only the parts that decide something are covered here.
/// </summary>
public sealed class PairingPromptTests
{
    [Theory]
    [InlineData("482913", "482913")]
    [InlineData("482 913", "482913")]
    [InlineData("482-913", "482913")]
    [InlineData("1234567", "123456")]
    [InlineData("12a", "12")]
    [InlineData("", "")]
    public void OnlyTheFirstSixDigitsAreKept(string typed, string kept)
    {
        Assert.Equal(kept, PairingPrompt.Digits(typed));
    }

    [Theory]
    [InlineData("482 913", true)]
    [InlineData("48291", false)]
    [InlineData("", false)]
    public void PairWaitsForAWholeCode(string typed, bool complete)
    {
        Assert.Equal(complete, PairingPrompt.Complete(typed));
    }

    [Theory]
    [InlineData(60_000, 0, 60)]
    [InlineData(60_000, 1, 60)]
    [InlineData(60_000, 59_001, 1)]
    [InlineData(60_000, 60_000, 0)]
    [InlineData(60_000, 90_000, 0)]
    public void TheCountdownRoundsUpAndStopsAtZero(long deadline, long now, int seconds)
    {
        Assert.Equal(seconds, PairingPrompt.SecondsLeft(deadline, now));
    }
}
