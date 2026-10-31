using Edgepad.Macros;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The delay is instant here, so no test sleeps and none depends on how fast a machine is.</summary>
public sealed class MacroLaunchFollowTests
{
    private readonly List<string> logged = [];
    private int raises;
    private int delays;

    private Task Follow(Func<OpenState> survey) => MacroLaunchFollow.Run(
        survey,
        () =>
        {
            raises++;
            return true;
        },
        _ =>
        {
            delays++;
            return Task.CompletedTask;
        },
        logged.Add);

    [Fact]
    public async Task AWindowThatAppearsBehindIsRaisedOnce()
    {
        var states = new Queue<OpenState>([OpenState.Closed, OpenState.Closed, OpenState.Open, OpenState.Open]);

        await Follow(states.Dequeue);

        Assert.Equal(1, raises);
        Assert.Contains("in front: no, raised: yes", Assert.Single(logged), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWindowThatAppearsInFrontIsNeverRaised()
    {
        var states = new Queue<OpenState>([OpenState.Closed, OpenState.Front]);

        await Follow(states.Dequeue);

        Assert.Equal(0, raises);
        Assert.Contains("in front: yes", Assert.Single(logged), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWindowThatNeverAppearsEndsTheFollowAndSaysSo()
    {
        await Follow(() => OpenState.Closed);

        Assert.Equal(0, raises);
        Assert.True(delays > 0);
        Assert.Contains("no window appeared", Assert.Single(logged), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASurveyThatThrowsDoesNotEscape()
    {
        await Follow(() => throw new InvalidOperationException("no desktop"));

        Assert.Equal(0, raises);
        Assert.Contains("no desktop", Assert.Single(logged), StringComparison.Ordinal);
    }
}
