using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// What the pairing question keeps of what the owner types, its countdown, and when it is put up and taken down. The
/// window itself needs a display, so only the parts that decide something are covered here.
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
    [InlineData(null, "")]
    public void OnlyTheFirstSixDigitsAreKept(string? typed, string kept)
    {
        Assert.Equal(kept, PairingPrompt.Digits(typed));
    }

    [Theory]
    [InlineData("482 913", true)]
    [InlineData("48291", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PairWaitsForAWholeCode(string? typed, bool complete)
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

    [Fact]
    public void TheQuestionNamesThePhoneAndOpensOnTheUiThreadWithinTheOwnersTime()
    {
        var queue = new Queue<Action>();
        var announced = new List<string>();
        string? opened = null;
        long deadline = 0;
        var prompt = new PairingPrompt(queue.Enqueue, announced.Add, (phone, until, _, _, _) =>
        {
            opened = phone;
            deadline = until;
        });
        var before = Environment.TickCount64;

        using var handle = prompt.Show("Pixel", _ => { }, () => { });

        // Announced at once; the window waits for the UI thread to get to it.
        Assert.Equal("Pixel", Assert.Single(announced));
        Assert.Null(opened);
        queue.Dequeue()();
        Assert.Equal("Pixel", opened);
        Assert.InRange(deadline - before, 59_000, 61_000);
    }

    [Fact]
    public void DisposingTheHandleWithdrawsAnOpenQuestion()
    {
        var queue = new Queue<Action>();
        CancellationToken seen = default;
        var prompt = new PairingPrompt(queue.Enqueue, _ => { }, (_, _, _, _, withdrawn) => seen = withdrawn);

        var handle = prompt.Show("Pixel", _ => { }, () => { });
        queue.Dequeue()();
        Assert.False(seen.IsCancellationRequested);

        handle.Dispose();
        Assert.True(seen.IsCancellationRequested);
    }

    [Fact]
    public void AQuestionWithdrawnBeforeTheUiGotToItIsNeverPut()
    {
        var queue = new Queue<Action>();
        var opened = false;
        var prompt = new PairingPrompt(queue.Enqueue, _ => { }, (_, _, _, _, _) => opened = true);

        prompt.Show("Pixel", _ => { }, () => { }).Dispose();
        queue.Dequeue()();

        Assert.False(opened);
    }
}
