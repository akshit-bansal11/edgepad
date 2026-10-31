using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class CoalescerTests
{
    [Fact]
    public void ABurstOfValuesForOneKeyBecomesTheNewest()
    {
        var queue = new Coalescer<string, int>();
        for (var value = 1; value <= 40; value++)
        {
            queue.Post("volume", value);
        }

        var taken = queue.Take();
        Assert.Single(taken);
        Assert.Equal("volume", taken[0].Key);
        Assert.Equal(40, taken[0].Value);
    }

    [Fact]
    public void KeysKeepTheirOwnNewestValueInTheOrderTheyFirstArrived()
    {
        var queue = new Coalescer<string, int>();
        queue.Post("volume", 10);
        queue.Post("mic", 5);
        queue.Post("volume", 20);
        queue.Post("brightness", 70);
        queue.Post("mic", 6);

        Assert.Equal(
            new[] { ("volume", 20), ("mic", 6), ("brightness", 70) },
            queue.Take().Select(pair => (pair.Key, pair.Value)));
    }

    [Fact]
    public void TakingEmptiesTheQueueAndALaterPostStartsFresh()
    {
        var queue = new Coalescer<string, int>();
        queue.Post("volume", 1);
        Assert.Single(queue.Take());
        Assert.Empty(queue.Take());

        queue.Post("volume", 2);
        queue.Post("mic", 3);
        Assert.Equal(["volume", "mic"], queue.Take().Select(pair => pair.Key));
    }

    [Fact]
    public void PostsFromManyThreadsAreAllAccountedFor()
    {
        var queue = new Coalescer<int, int>();
        Parallel.For(0, 8, key =>
        {
            for (var value = 0; value < 1000; value++)
            {
                queue.Post(key, value);
            }
        });

        var taken = queue.Take();
        Assert.Equal(8, taken.Count);
        Assert.All(taken, pair => Assert.Equal(999, pair.Value));
    }
}
