using System.Net;
using System.Net.Sockets;

namespace Edgepad.Net;

/// <summary>
/// Accepts the TCP connections of Wi-Fi and USB phones and hands each socket on. At most <see cref="MaxUnauthenticated"/>
/// connections may be in the unauthenticated phase at once (anyone on the network can open sockets), and each has a
/// read and write deadline until its owner calls the <c>done</c> it was given, which is when the link layer has
/// finished with it. A connection over the cap is closed at once. Nothing here lets an exception end a thread.
/// </summary>
/// <param name="requestedPort">The port to listen on; 0 for any free one.</param>
/// <param name="accepted">
/// Called on a thread of its own per connection, with the socket and a <c>done</c> to call once the connection is
/// authenticated or refused (calling it again does nothing). The callback owns the socket and runs for as long as the
/// connection does; when it returns, <c>done</c> is called for it.
/// </param>
/// <param name="deadline">How long a read or write may take before <c>done</c>; ten seconds by default.</param>
internal sealed class TcpLinkListener(int requestedPort, Action<Socket, Action> accepted, TimeSpan? deadline = null) : IDisposable
{
    public const int MaxUnauthenticated = 4;

    private readonly Lock gate = new();
    private readonly int deadlineMs = (int)(deadline ?? TimeSpan.FromSeconds(10)).TotalMilliseconds;

    private Socket? listener;
    private Thread? thread;
    private int unauthenticated;
    private volatile bool stopped;

    /// <summary>The port actually listened on: the requested one, or the one the system gave when that was taken.</summary>
    public int Port { get; private set; }

    /// <summary>False, logged, when no port could be opened.</summary>
    public bool Start()
    {
        lock (gate)
        {
            if (stopped || listener is not null)
            {
                return false;
            }

            Socket opened;
            try
            {
                opened = Open(requestedPort);
            }
            catch (SocketException e) when (requestedPort != 0)
            {
                Log.Write($"Port {requestedPort} would not open ({e.Message}), so a free one is used");
                try
                {
                    opened = Open(0);
                }
                catch (SocketException again)
                {
                    Log.Write($"No port would open for Wi-Fi and USB phones: {again.Message}");
                    return false;
                }
            }
            catch (SocketException e)
            {
                Log.Write($"No port would open for Wi-Fi and USB phones: {e.Message}");
                return false;
            }

            listener = opened;
            Port = ((IPEndPoint)opened.LocalEndPoint!).Port;
            thread = new Thread(() => Accept(opened)) { IsBackground = true, Name = "edgepad-tcp-accept" };
            thread.Start();
            return true;
        }
    }

    public void Dispose()
    {
        Thread? accepting;
        lock (gate)
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            listener?.Dispose();
            accepting = thread;
        }

        if (accepting is not null && accepting != Thread.CurrentThread && !accepting.Join(TimeSpan.FromSeconds(2)))
        {
            Log.Write("The TCP accept thread did not stop within 2 s");
        }
    }

    // Dual-stack where the machine has IPv6, plain IPv4 where it does not.
    private static Socket Open(int port)
    {
        var dual = Socket.OSSupportsIPv6;
        var socket = new Socket(dual ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            if (dual)
            {
                socket.DualMode = true;
            }

            socket.Bind(new IPEndPoint(dual ? IPAddress.IPv6Any : IPAddress.Any, port));
            socket.Listen(16);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private void Accept(Socket from)
    {
        while (!stopped)
        {
            Socket client;
            try
            {
                client = from.Accept();
            }
            catch (Exception e)
            {
                if (stopped)
                {
                    return;
                }

                // A reset in the backlog or a descriptor shortage must not end the loop, or the thread.
                Log.Write($"A TCP connection could not be accepted: {e.Message}");
                Thread.Sleep(100);
                continue;
            }

            Admit(client);
        }
    }

    private void Admit(Socket client)
    {
        if (Interlocked.Increment(ref unauthenticated) > MaxUnauthenticated)
        {
            Interlocked.Decrement(ref unauthenticated);
            client.Dispose();
            return;
        }

        var released = 0;
        void Done()
        {
            if (Interlocked.Exchange(ref released, 1) != 0)
            {
                return;
            }

            try
            {
                client.ReceiveTimeout = 0;
                client.SendTimeout = 0;
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                // Already closed: nothing left to relax.
            }

            Interlocked.Decrement(ref unauthenticated);
        }

        try
        {
            client.NoDelay = true;
            client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            client.ReceiveTimeout = deadlineMs;
            client.SendTimeout = deadlineMs;
            new Thread(() => Run(client, Done)) { IsBackground = true, Name = "edgepad-tcp-link" }.Start();
        }
        catch (Exception e)
        {
            Log.Write($"A TCP connection could not be set up: {e.Message}");
            Done();
            client.Dispose();
        }
    }

    private void Run(Socket client, Action done)
    {
        try
        {
            accepted(client, done);
        }
        catch (Exception e)
        {
            // A thread of its own: an escape ends the process.
            Log.Write($"A TCP link failed: {e.Message}");
            client.Dispose();
        }
        finally
        {
            done();
        }
    }
}
