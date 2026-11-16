using System.Collections.Concurrent;
using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class WorkerTests
{
    [Fact]
    public void JobsRunInOrderAndAThrowDoesNotStopTheRest()
    {
        var ran = new ConcurrentQueue<int>();
        using var done = new ManualResetEventSlim();
        using var worker = new Worker((_, _) => { });
        worker.Post(() => ran.Enqueue(1));
        worker.Post(() => throw new InvalidOperationException("job"));
        worker.Post(() => ran.Enqueue(2));
        worker.Post(done.Set);
        Assert.True(done.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal([1, 2], ran);
    }
}
