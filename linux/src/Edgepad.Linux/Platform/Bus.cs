using System.Net.Sockets;
using Tmds.DBus.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// A lazily opened D-Bus connection for calls made from a worker thread (never the session's reader): one call at a
/// time, each with a deadline so a hung service cannot hold the worker, and a connection that is dropped and opened
/// again after any failure, since a bus that went away (a logind restart, a session bus that was not up yet) is the
/// ordinary case and not an error to give up on. A failed call answers false and is logged once per distinct reason.
/// </summary>
internal sealed class Bus(Func<string?> address, string name) : IDisposable
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    private readonly Lock gate = new();
    private DBusConnection? connection;
    private string? lastFailure;

    public static Bus OnSystem() => new(() => DBusAddress.System, "system");

    public static Bus OnSession() => new(() => DBusAddress.Session, "session");

    /// <summary>Makes the call and ignores any return value. False when it failed.</summary>
    public bool Call(Func<DBusConnection, MessageBuffer> build) =>
        Attempt(c => c.CallMethodAsync(build(c)));

    /// <summary>Makes the call and reads its reply with <paramref name="reader"/>. False when it failed.</summary>
    public bool Call<T>(Func<DBusConnection, MessageBuffer> build, MessageValueReader<T> reader, out T? value)
    {
        T? result = default;
        var ok = Attempt(async c =>
        {
            result = await c.CallMethodAsync(build(c), reader);
        });
        value = ok ? result : default;
        return ok;
    }

    public void Dispose()
    {
        lock (gate)
        {
            Reset();
        }
    }

    private bool Attempt(Func<DBusConnection, Task> call)
    {
        lock (gate)
        {
            try
            {
                if (connection is null)
                {
                    var created = new DBusConnection(address() ?? throw new IOException($"The {name} bus address is unknown"));
                    connection = created;
                    if (!created.ConnectAsync().AsTask().Wait(Deadline))
                    {
                        throw new TimeoutException($"The {name} bus did not answer");
                    }
                }

                if (!call(connection).Wait(Deadline))
                {
                    throw new TimeoutException($"A call on the {name} bus was not answered");
                }

                return true;
            }
            catch (Exception e) when (Unwrap(e) is DBusErrorReplyException)
            {
                // The service answered with an error (a player that just quit, a method it does not have): the
                // connection is fine, and the caller sees only that this call did not work.
                return false;
            }
            catch (Exception e) when (Unwrap(e) is DBusExceptionBase or IOException or SocketException or TimeoutException
                or ObjectDisposedException or InvalidOperationException)
            {
                // The connection is dropped whatever went wrong, and opened again by the next call.
                Reset();
                var reason = Unwrap(e).Message;
                if (reason != lastFailure)
                {
                    lastFailure = reason;
                    Log.Write($"A call on the {name} bus failed: {reason}");
                }

                return false;
            }
        }
    }

    private static Exception Unwrap(Exception e) => e is AggregateException { InnerException: { } inner } ? inner : e;

    private void Reset()
    {
        connection?.Dispose();
        connection = null;
    }
}
