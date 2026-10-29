using Edgepad.Net;
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
        var prompt = new PairingPrompt(queue.Enqueue, announced.Add, (phone, until, _, _) =>
        {
            opened = phone;
            deadline = until;
            return new Window();
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
    public void WhatThePairingTellsTheQuestionReachesTheWindowOnTheUiThreadInOrder()
    {
        var queue = new Queue<Action>();
        using var window = new Window();
        var prompt = new PairingPrompt(queue.Enqueue, _ => { }, (_, _, _, _) => window);

        var handle = prompt.Show("Pixel", _ => { }, () => { });
        handle.Matched();
        handle.Finished(paired: true);
        handle.Dispose();

        // Nothing reaches the window off the UI thread.
        Assert.Empty(window.Told);
        while (queue.Count > 0)
        {
            queue.Dequeue()();
        }

        Assert.Equal(["matched", "finished:True", "withdrawn"], window.Told);
    }

    [Fact]
    public void AMismatchIsPassedOn()
    {
        var queue = new Queue<Action>();
        using var window = new Window();
        var prompt = new PairingPrompt(queue.Enqueue, _ => { }, (_, _, _, _) => window);

        using (var handle = prompt.Show("Pixel", _ => { }, () => { }))
        {
            handle.Mismatched();
        }

        while (queue.Count > 0)
        {
            queue.Dequeue()();
        }

        Assert.Equal(["mismatched", "withdrawn"], window.Told);
    }

    [Fact]
    public void AQuestionWithdrawnBeforeTheUiGotToItIsNeverPut()
    {
        var queue = new Queue<Action>();
        var opened = false;
        var prompt = new PairingPrompt(queue.Enqueue, _ => { }, (_, _, _, _) =>
        {
            opened = true;
            return new Window();
        });

        var handle = prompt.Show("Pixel", _ => { }, () => { });
        handle.Dispose();
        handle.Finished(paired: false);
        while (queue.Count > 0)
        {
            queue.Dequeue()();
        }

        Assert.False(opened);
    }

    /// <summary>The window, as far as the pairing can tell it anything.</summary>
    private sealed class Window : IPairingQuestion
    {
        public List<string> Told { get; } = [];

        public void Matched() => Told.Add("matched");

        public void Mismatched() => Told.Add("mismatched");

        public void Finished(bool paired) => Told.Add($"finished:{paired}");

        public void Dispose() => Told.Add("withdrawn");
    }
}
