using System.Security.Cryptography;
using System.Text;
using Edgepad.Dashboard;
using Edgepad.Protocol;

namespace Edgepad.Hooks;

/// <summary>
/// The tray app's end of the local hook channel. Per connection: one request line of at most 64 KiB, which must arrive within
/// 2 s; the token compared in constant time; then one reply and close. Anything malformed, from an unknown agent or kind, or
/// with a wrong token is closed with no reply. At most 8 connections at once; the 9th is closed unread. A request for an agent
/// whose switch is off is answered (perm: defer) and otherwise ignored. Each accepted request stamps the agent's last-heard
/// time; the first accepted Codex request records that the person trusted the plugin's hooks. Only the agent, the kind and the
/// outcome are ever logged, never a command, path or token.
/// </summary>
internal sealed class HookServer : IDisposable
{
    public const int MaxConnections = 8;
    public static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan WriteDeadline = TimeSpan.FromSeconds(2);

    private readonly IHookListener listener;
    private readonly Func<string> token;
    private readonly PermissionBroker broker;
    private readonly SessionBoard sessions;
    private readonly LimitsBoard limits;
    private readonly SpendBoard spend;
    private readonly UsageStore usage;
    private readonly IntegrationSettings settings;
    private readonly Func<DateTimeOffset> clock;
    private readonly TimeSpan readDeadline;
    private readonly CancellationTokenSource stop = new();
    private Task? loop;
    private int active;

    /// <param name="token">The current token (<see cref="HookEndpoint.Token"/>), read per request so a rotation applies at once.</param>
    /// <param name="settings">Whose switches say which agent is on (keys <c>claude</c> and <c>codex</c>) and where codexTrusted is kept.</param>
    public HookServer(
        IHookListener listener,
        Func<string> token,
        PermissionBroker broker,
        SessionBoard sessions,
        LimitsBoard limits,
        SpendBoard spend,
        UsageStore usage,
        IntegrationSettings settings,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? readDeadline = null)
    {
        this.listener = listener;
        this.token = token;
        this.broker = broker;
        this.sessions = sessions;
        this.limits = limits;
        this.spend = spend;
        this.usage = usage;
        this.settings = settings;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.readDeadline = readDeadline ?? ReadDeadline;
    }

    /// <summary>
    /// An agent now works with Edgepad: its first accepted request while it had no last-heard stamp, or for Codex the first after
    /// Connect (the person trusted the plugin's hooks). Raised on a hook thread with the agent's token.
    /// </summary>
    public event Action<string>? AgentReady;

    /// <summary>Connections being served now.</summary>
    public int Active => Volatile.Read(ref active);

    public void Start() => loop ??= Task.Run(AcceptLoop);

    public void Dispose()
    {
        if (stop.IsCancellationRequested)
        {
            return;
        }

        stop.Cancel();
        listener.Dispose();
        try
        {
            loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The loop ends by cancellation; nothing to report.
        }

        stop.Dispose();
    }

    private async Task AcceptLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            Stream? client;
            try
            {
                client = await listener.AcceptAsync(stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                if (stop.IsCancellationRequested)
                {
                    return;
                }

                Log.Write($"A hook connection could not be accepted: {e.GetType().Name}");
                try
                {
                    await Task.Delay(100, stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            if (client is null)
            {
                return;
            }

            if (Interlocked.Increment(ref active) > MaxConnections)
            {
                Interlocked.Decrement(ref active);
                await client.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => ServeGuarded(client));
        }
    }

    private async Task ServeGuarded(Stream client)
    {
        try
        {
            await Serve(client).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // A task of its own: nothing may escape. The type only: the message could quote the request.
            Log.Write($"A hook connection failed: {e.GetType().Name}");
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
            Interlocked.Decrement(ref active);
        }
    }

    private async Task Serve(Stream client)
    {
        var line = await ReadLine(client).ConfigureAwait(false);
        if (line is null || HookProtocol.TryParse(line) is not { } request || !TokenMatches(request.Token))
        {
            return;
        }

        if (!settings.Flag(request.Agent))
        {
            await Reply(client, request is PermRequest ? HookProtocol.DecisionReply(HookProtocol.Defer) : HookProtocol.OkReply()).ConfigureAwait(false);
            return;
        }

        Heard(request.Agent);
        switch (request)
        {
            case PermRequest perm:
                await ServePerm(client, perm).ConfigureAwait(false);
                return;
            case SessionRequest session:
                var hook = session.Agent switch
                {
                    AgentToken.Claude => HookEvent.FromClaude(session.Event, session.NotificationType, session.Session, session.Cwd),
                    AgentToken.Gemini => HookEvent.FromGemini(session.Event, session.NotificationType, session.Session, session.Cwd),
                    AgentToken.OpenCode => HookEvent.FromOpenCode(session.Event, session.Session, session.Cwd),
                    _ => HookEvent.FromCodex(session.Event, session.Session, session.Cwd),
                };
                if (hook is not null)
                {
                    sessions.Apply(hook);
                }

                break;
            case UsageRequest u:
                limits.Update(u.Agent, u.Windows, u.ContextPct, clock());
                break;
            case SpendRequest s:
                spend.Update(s.Agent, s.Totals, clock());
                break;
        }

        await Reply(client, HookProtocol.OkReply()).ConfigureAwait(false);
    }

    private async Task ServePerm(Stream client, PermRequest perm)
    {
        // The hook leaving (answered in the terminal, cancelled) ends the ask: a read that returns ends this token.
        using var left = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        var watch = WatchClose(client, left);
        var decision = await broker.RequestAsync(perm, left.Token).ConfigureAwait(false);
        var word = decision switch
        {
            PermDecision.Allow => HookProtocol.Allow,
            PermDecision.Deny => HookProtocol.Deny,
            _ => HookProtocol.Defer,
        };
        Log.Write($"Hook perm from {perm.Agent}: {word}");
        if (!left.IsCancellationRequested)
        {
            await Reply(client, HookProtocol.DecisionReply(word)).ConfigureAwait(false);
        }

        await left.CancelAsync().ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);
        await watch.ConfigureAwait(false);
    }

    // Completes when the client closes its end (or sends more, which is not allowed either) and cancels `left` then.
    private static async Task WatchClose(Stream client, CancellationTokenSource left)
    {
        var one = new byte[1];
        try
        {
            await client.ReadAsync(one, left.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException or TimeoutException)
        {
            // Closed under us: the same as the client leaving.
        }

        try
        {
            await left.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Already over.
        }
    }

    // One line of at most 64 KiB without its line feed, or null: too long, closed first, or not complete within the deadline.
    // The deadline closes the stream, which ends a blocked read on any kind of stream.
    private async Task<byte[]?> ReadLine(Stream client)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        deadline.CancelAfter(readDeadline);
        using var closer = deadline.Token.Register(client.Dispose);
        var buffer = new byte[HookProtocol.MaxLineBytes + 1];
        var filled = 0;
        try
        {
            while (filled < buffer.Length)
            {
                var n = await client.ReadAsync(buffer.AsMemory(filled), deadline.Token).ConfigureAwait(false);
                if (n == 0)
                {
                    return null;
                }

                var end = Array.IndexOf(buffer, (byte)'\n', filled, n);
                filled += n;
                if (end >= 0)
                {
                    return deadline.IsCancellationRequested ? null : buffer[..end];
                }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or TimeoutException)
        {
            return null;
        }

        return null;
    }

    private bool TokenMatches(string presented) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(token()));

    private void Heard(string agent)
    {
        var ready = usage.Touch(agent, clock());
        if (agent == AgentToken.Codex && !settings.Flag(IntegrationSettings.CodexTrusted))
        {
            settings.SetFlag(IntegrationSettings.CodexTrusted, true);
            ready = true;
        }

        if (ready)
        {
            AgentReady?.Invoke(agent);
        }
    }

    // A client that already left makes the write fail; that is its choice, not an error.
    private async Task Reply(Stream client, byte[] reply)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        deadline.CancelAfter(WriteDeadline);
        try
        {
            await client.WriteAsync(reply, deadline.Token).ConfigureAwait(false);
            await client.FlushAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or NotSupportedException)
        {
            // The client left first.
        }
    }
}
