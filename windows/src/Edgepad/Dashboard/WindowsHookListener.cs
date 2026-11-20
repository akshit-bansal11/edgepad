using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Edgepad.Hooks;

namespace Edgepad.Dashboard;

/// <summary>
/// The hook channel on Windows: the named pipe <c>\\.\pipe\edgepad-hook-&lt;16 hex of SHA-256(user SID)&gt;</c>, byte mode, at
/// most <see cref="MaxInstances"/> instances. Every instance carries the same protected DACL: full control for this user and
/// nobody else, and a deny for NETWORK, with this user as owner; .NET also rejects remote clients. The first instance is created
/// with FILE_FLAG_FIRST_PIPE_INSTANCE, so a pipe of that name made first by any other process (another user squatting it) makes
/// the constructor throw instead of sharing the name. One instance always waits; when a client connects the next is made before
/// the connected one is handed out, so a burst of hooks finds a free instance. Who may connect is the ACL's job; what a client
/// may say is HookServer's (token, size, deadline).
/// </summary>
/// <remarks>
/// <see cref="PipeOptions.CurrentUserOnly"/> is not passed: on the server it only builds an owner-only DACL, which .NET then
/// either refuses beside an explicit <see cref="PipeSecurity"/> or puts in its place, dropping the NETWORK deny. The DACL
/// written here is that one plus the deny.
/// </remarks>
internal sealed class WindowsHookListener : IHookListener
{
    public const int MaxInstances = 8;
    private const int BufferBytes = 4096;
    private static readonly TimeSpan BusyWait = TimeSpan.FromMilliseconds(100);

    private readonly string name;
    private readonly PipeSecurity security;
    private readonly Lock gate = new();

    // Under gate. Instances handed out, until their user closes them: while one is open the name is still ours.
    private readonly List<NamedPipeServerStream> served = [];

    private NamedPipeServerStream? waiting;
    private bool disposed;
    private bool warned;

    /// <param name="pipeName">For tests: a name of their own. The current user's <see cref="HookEndpoint.PipeName"/> by default.</param>
    /// <exception cref="IOException">The pipe could not be made (or another process already holds its name).</exception>
    /// <exception cref="UnauthorizedAccessException">Another user's process holds the name.</exception>
    public WindowsHookListener(string? pipeName = null)
    {
        var user = CurrentUser();
        name = pipeName ?? HookEndpoint.PipeName(user.Value);
        Endpoint = @"\\.\pipe\" + name;
        security = Security(user);
        waiting = Create(first: true);
    }

    public string Endpoint { get; }

    /// <summary>The DACL every instance gets: one allow (full control) for <paramref name="user"/>, one deny for NETWORK, nothing inherited.</summary>
    internal static PipeSecurity Security(SecurityIdentifier user)
    {
        var acl = new PipeSecurity();
        acl.SetOwner(user);
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        acl.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return acl;
    }

    internal static SecurityIdentifier CurrentUser()
    {
        using var me = WindowsIdentity.GetCurrent();
        return me.User ?? throw new IOException("The current user has no SID");
    }

    public async Task<Stream?> AcceptAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            NamedPipeServerStream? server;
            lock (gate)
            {
                if (disposed)
                {
                    return null;
                }

                waiting ??= TryCreate();
                server = waiting;
            }

            if (server is null)
            {
                // All instances are serving: HookServer's own cap. Wait for one to close.
                await Task.Delay(BusyWait, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
            catch (IOException)
            {
                // A client that connected and left before it was seen: this instance is spent, make another.
                Forget(server);
                continue;
            }

            lock (gate)
            {
                if (ReferenceEquals(waiting, server))
                {
                    waiting = null;
                }

                if (disposed)
                {
                    server.Dispose();
                    return null;
                }

                served.Add(server);
                waiting = TryCreate();
            }

            return server;
        }
    }

    public void Dispose()
    {
        NamedPipeServerStream? last;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            last = waiting;
            waiting = null;
        }

        last?.Dispose();
    }

    private void Forget(NamedPipeServerStream server)
    {
        lock (gate)
        {
            if (ReferenceEquals(waiting, server))
            {
                waiting = null;
            }
        }

        server.Dispose();
    }

    // Under gate. Null when every instance is taken (ERROR_PIPE_BUSY); anything else is logged by type and also gives null, retried
    // later. With no instance of ours left open the name is free for anyone, so the new one must again be the first instance: a
    // pipe another process made in the gap then makes this throw instead of being shared.
    private NamedPipeServerStream? TryCreate()
    {
        try
        {
            _ = served.RemoveAll(IsClosed);
            return Create(first: served.Count == 0);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException e)
        {
            if (!warned)
            {
                warned = true;
                Log.Write($"A hook pipe instance could not be made: {e.GetType().Name}");
            }

            return null;
        }
    }

    private static bool IsClosed(NamedPipeServerStream stream)
    {
        try
        {
            return stream.SafePipeHandle.IsClosed;
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or IOException)
        {
            return true;
        }
    }

    private NamedPipeServerStream Create(bool first) =>
        NamedPipeServerStreamAcl.Create(
            name,
            PipeDirection.InOut,
            MaxInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None),
            BufferBytes,
            BufferBytes,
            security);
}
