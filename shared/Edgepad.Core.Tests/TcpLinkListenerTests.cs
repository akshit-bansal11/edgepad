using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

public sealed class TcpLinkListenerTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly ConcurrentQueue<(Socket Socket, Action Done)> accepted = new();

    // The callback runs for as long as its connection does, so this holds it open until the test is over.
    private readonly ManualResetEventSlim over = new();

    private TcpLinkListener Listen(TimeSpan? deadline = null)
    {
        var listener = new TcpLinkListener(0, (socket, done) =>
        {
            accepted.Enqueue((socket, done));
            over.Wait();
        }, deadline);
        Assert.True(listener.Start());
        return listener;
    }

    private static async Task<TcpClient> ConnectAsync(TcpLinkListener listener)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, listener.Port, TestContext.Current.CancellationToken);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task WaitUntilAsync(Func<int> count, int expected)
    {
        var until = DateTime.UtcNow + Patience;
        while (count() < expected)
        {
            Assert.True(DateTime.UtcNow < until, $"Only {count()} of {expected} connections were handed on");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private Task WaitForAsync(int expected) => WaitUntilAsync(() => accepted.Count, expected);

    // Zero on a clean close; a reset counts as closed too.
    private static async Task<int> ReadUntilClosedAsync(TcpClient client)
    {
        try
        {
            return await client.GetStream().ReadAsync(new byte[1], TestContext.Current.CancellationToken).AsTask().WaitAsync(Patience, TestContext.Current.CancellationToken);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    [Fact]
    public async Task AConnectionIsHandedOnWithItsSocket()
    {
        using var listener = Listen();
        using var client = await ConnectAsync(listener);

        await WaitForAsync(1);

        Assert.True(accepted.TryPeek(out var first));
        Assert.True(first.Socket.NoDelay);
    }

    [Fact]
    public async Task AFifthUnauthenticatedConnectionIsClosedAtOnce()
    {
        using var listener = Listen();
        var clients = new List<TcpClient>();
        try
        {
            for (var i = 0; i < TcpLinkListener.MaxUnauthenticated; i++)
            {
                clients.Add(await ConnectAsync(listener));
            }

            await WaitForAsync(TcpLinkListener.MaxUnauthenticated);
            using var fifth = await ConnectAsync(listener);

            Assert.Equal(0, await ReadUntilClosedAsync(fifth));
            Assert.Equal(TcpLinkListener.MaxUnauthenticated, accepted.Count);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task AFinishedConnectionFreesItsPlace()
    {
        using var listener = Listen();
        var clients = new List<TcpClient>();
        try
        {
            for (var i = 0; i < TcpLinkListener.MaxUnauthenticated; i++)
            {
                clients.Add(await ConnectAsync(listener));
            }

            await WaitForAsync(TcpLinkListener.MaxUnauthenticated);
            Assert.True(accepted.TryPeek(out var first));
            first.Done();
            first.Done();
            clients.Add(await ConnectAsync(listener));

            await WaitForAsync(TcpLinkListener.MaxUnauthenticated + 1);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task ASilentConnectionTimesOutOnItsRead()
    {
        using var listener = Listen(TimeSpan.FromMilliseconds(200));
        using var client = await ConnectAsync(listener);
        await WaitForAsync(1);
        Assert.True(accepted.TryPeek(out var server));

        var error = Assert.Throws<SocketException>(() => server.Socket.Receive(new byte[1]));

        Assert.Equal(SocketError.TimedOut, error.SocketErrorCode);
    }

    [Fact]
    public async Task DoneLiftsTheDeadline()
    {
        using var listener = Listen(TimeSpan.FromMilliseconds(100));
        using var client = await ConnectAsync(listener);
        await WaitForAsync(1);
        Assert.True(accepted.TryPeek(out var server));

        server.Done();

        Assert.Equal(0, server.Socket.ReceiveTimeout);
    }

    [Fact]
    public async Task ACallbackThatThrowsClosesItsConnectionAndTheListenerKeepsAccepting()
    {
        var calls = 0;
        using var listener = new TcpLinkListener(0, (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("boom");
            }
        });
        Assert.True(listener.Start());
        using var first = await ConnectAsync(listener);
        Assert.Equal(0, await ReadUntilClosedAsync(first));

        using var second = await ConnectAsync(listener);

        await WaitUntilAsync(() => Volatile.Read(ref calls), 2);
    }

    [Fact]
    public void ATakenPortFallsBackToAFreeOne()
    {
        using var listener = Listen();
        using var second = new TcpLinkListener(listener.Port, (_, _) => { });

        Assert.True(second.Start());
        Assert.NotEqual(listener.Port, second.Port);
    }

    public void Dispose()
    {
        over.Set();
        foreach (var (socket, _) in accepted)
        {
            socket.Dispose();
        }

        over.Dispose();
    }
}
