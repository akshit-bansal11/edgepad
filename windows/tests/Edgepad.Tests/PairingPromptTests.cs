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

    [Fact]
    public void AQuestionWithdrawnBeforeTheUiGotToItIsNeverPut()
    {
        var ui = new Queued();
        var announced = new List<string>();
        var prompt = new PairingPrompt(ui, announced.Add);

        prompt.Show("Pixel", _ => { }, () => { }).Dispose();
        ui.RunAll();

        // The name is announced at once; the window, which would be an open form, never went up.
        Assert.Equal("Pixel", Assert.Single(announced));
        Assert.Empty(Application.OpenForms);
    }

    /// <summary>A UI thread that runs what was posted to it only when the test says so, in order.</summary>
    private sealed class Queued : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Work, object? State)> posted = new();

        public override void Post(SendOrPostCallback d, object? state) => posted.Enqueue((d, state));

        public void RunAll()
        {
            while (posted.TryDequeue(out var next))
            {
                next.Work(next.State);
            }
        }
    }
}
