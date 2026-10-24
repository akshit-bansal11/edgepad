using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The trust question's decision, without a window: only a clear yes inside the timeout trusts, and everything
/// else (silence, a failure, a question that could not be put) refuses.
/// </summary>
public sealed class TrustPromptTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void AYesTrusts() =>
        Assert.True(new TrustPrompt((_, _) => Task.FromResult(true), Short).Ask("AA:AA:AA:AA:AA:AA"));

    [Fact]
    public void ANoRefuses() =>
        Assert.False(new TrustPrompt((_, _) => Task.FromResult(false), Short).Ask("AA:AA:AA:AA:AA:AA"));

    [Fact]
    public void NoAnswerInTimeRefusesAndWithdrawsTheQuestion()
    {
        CancellationToken seen = default;
        var never = new TaskCompletionSource<bool>();
        var prompt = new TrustPrompt(
            (_, token) =>
            {
                seen = token;
                return never.Task;
            },
            Short);

        Assert.False(prompt.Ask("AA:AA:AA:AA:AA:AA"));
        Assert.True(seen.IsCancellationRequested);
    }

    [Fact]
    public void ALateYesIsTooLate()
    {
        var late = new TaskCompletionSource<bool>();
        var prompt = new TrustPrompt((_, _) => late.Task, Short);

        Assert.False(prompt.Ask("AA:AA:AA:AA:AA:AA"));
        late.SetResult(true);
    }

    [Fact]
    public void AQuestionThatThrowsRefuses() =>
        Assert.False(new TrustPrompt((_, _) => throw new InvalidOperationException("no display"), Short).Ask("AA:AA:AA:AA:AA:AA"));

    [Fact]
    public void AQuestionThatFailsLaterRefuses() =>
        Assert.False(new TrustPrompt((_, _) => Task.FromException<bool>(new InvalidOperationException("window failed")), Short).Ask("AA:AA:AA:AA:AA:AA"));

    [Fact]
    public void TheDefaultsAreTheDocumentedOnes()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), TrustPrompt.DefaultTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), TrustPrompt.TrustDelay);
    }
}
