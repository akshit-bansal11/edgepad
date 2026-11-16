using Edgepad.Power;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class PowerReporterTests
{
    private static readonly TimeSpan Never = TimeSpan.FromHours(1);

    [Fact]
    public async Task DisposeReturnsWhileASendIsBlocked()
    {
        var ct = TestContext.Current.CancellationToken;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var r = new PowerReporter(
            () => [], () => null, _ =>
            {
                entered.Set();
                release.Wait(ct);
            },
            Never, Never);
        var poke = Task.Run(r.Poke, ct);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10), ct));
            await Task.Run(r.Dispose, ct).WaitAsync(TimeSpan.FromSeconds(1), ct);
        }
        finally
        {
            release.Set();
        }

        await poke.WaitAsync(TimeSpan.FromSeconds(10), ct);
    }

    [Fact]
    public async Task ConcurrentPokesSendEachTextOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var sent = new List<Text>();
        using var release = new ManualResetEventSlim();
        using var r = new PowerReporter(
            () => [new("laptop", 50, false, "L")], () => ("speaker", "Speakers"), t =>
            {
                release.Wait(ct);
                sent.Add(t);
            },
            Never, Never);
        var first = Task.Run(r.Poke, ct);
        var second = Task.Run(r.Poke, ct);
        release.Set();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), ct);

        Assert.Single(sent, t => t.Kind == (byte)TextKind.Batteries);
        Assert.Single(sent, t => t.Kind == (byte)TextKind.Output);
    }

    [Fact]
    public void AfterDisposePokeSendsNothing()
    {
        var sent = new List<Text>();
        using var r = new PowerReporter(() => [], () => null, sent.Add, Never, Never);
        r.Dispose();
        r.Poke();
        Assert.Empty(sent);
    }
}
