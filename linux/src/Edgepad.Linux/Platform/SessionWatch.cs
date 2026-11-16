using Tmds.DBus.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// Keeps the phone out of every login session but this user's own. A fast user switch hands the seat, and with it
/// every input device, to another user's session, and a uinput device outlives logind's revoking of this user's
/// access: the phone's virtual keyboard and pad would type into the other user's desktop. So while logind says this
/// user's session is not the one in front, <c>hold</c> hangs up on the phone (which destroys its devices) and
/// refuses new ones, and <c>resume</c> lets them in again once it is back.
///
/// Read again whenever logind says a session's properties changed (PropertiesChanged on any session, since
/// <c>session/auto</c> is not the path signals come from), and otherwise every 10 s as a safety net, from a thread
/// of its own: a property read is one D-Bus call. Without the signal (no system bus to listen on, or it went away)
/// it polls twice a second as before. The session is logind's <c>session/auto</c>, the caller's own session or, for
/// an app that is not in one, the user's graphical session.
///
/// When logind cannot answer (no logind, as on a distribution without systemd or elogind, or no session for this
/// user) the session is taken to be in front. Failing closed there would leave Edgepad doing nothing at all on
/// those systems, where there is also no fast user switch for logind to report. Once the session has been seen in
/// the background, though, a failed read keeps the phone out until logind says it is back.
/// </summary>
internal sealed class SessionWatch : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SafetyNet = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ListenDeadline = TimeSpan.FromSeconds(2);

    private const string SessionPath = "/org/freedesktop/login1/session/auto";

    private readonly Bus bus = Bus.OnSystem();
    private readonly Action hold;
    private readonly Action resume;
    private readonly CancellationTokenSource stopping = new();
    private readonly AutoResetEvent changed = new(false);
    private readonly Thread thread;
    private bool holding;

    public SessionWatch(Action hold, Action resume)
    {
        this.hold = hold;
        this.resume = resume;
        thread = new Thread(Run) { IsBackground = true, Name = "edgepad-session-watch" };
        thread.Start();
    }

    /// <summary>
    /// Whether the phone is to be kept out, given whether it is now and what logind said: true for a session in the
    /// background, false for one in front, and no change when logind did not answer (<paramref name="active"/> null).
    /// </summary>
    public static bool Holds(bool holding, bool? active) => active is { } inFront ? !inFront : holding;

    /// <summary>
    /// Whether this session is in front, from its <c>org.freedesktop.login1.Session</c> properties: State is
    /// "active" exactly when it is; null without a State string.
    /// </summary>
    public static bool? ActiveOf(Dictionary<string, VariantValue>? properties) =>
        properties is not null && properties.TryGetValue("State", out var state) && state.Type == VariantValueType.String
            ? state.GetString() == "active"
            : null;

    /// <summary>How long to wait for the next read: the safety net while the change signal is heard, else the old poll.</summary>
    public static TimeSpan WaitFor(bool listening) => listening ? SafetyNet : Every;

    public void Dispose()
    {
        stopping.Cancel();
        if (thread.Join(JoinTimeout))
        {
            stopping.Dispose();
            changed.Dispose();
        }

        bus.Dispose();
    }

    private void Run()
    {
        DBusConnection? signals = null;
        try
        {
            signals = Listen();
            var lost = signals?.DisconnectedAsync();
            WaitHandle[] wake = [stopping.Token.WaitHandle, changed];
            while (!stopping.IsCancellationRequested)
            {
                var next = Holds(holding, Active());
                if (next != holding)
                {
                    holding = next;
                    if (next)
                    {
                        Log.Write("This user's session is no longer the one in front (another user switched in, or it is closing): hanging up on the phone and refusing phones until it is back");
                        hold();
                    }
                    else
                    {
                        Log.Write("This user's session is in front again; phones may connect");
                        resume();
                    }
                }

                _ = WaitHandle.WaitAny(wake, WaitFor(lost is { IsCompleted: false }));
            }
        }
        catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException)
        {
            // Quitting.
        }
        catch (Exception e)
        {
            // A bug here must not leave phones free to type into whichever session is in front: closed until restart.
            Log.Write($"The login session watch stopped, so phones are refused until Edgepad restarts: {e}");
            if (!holding)
            {
                holding = true;
                hold();
            }
        }
        finally
        {
            signals?.Dispose();
        }
    }

    /// <summary>
    /// A system bus connection that wakes the loop when any login session's properties change; null, logged, when it
    /// cannot be set up, and the loop then polls.
    /// </summary>
    private DBusConnection? Listen()
    {
        DBusConnection? connection = null;
        try
        {
            connection = new DBusConnection(DBusAddress.System ?? throw new IOException("The system bus address is unknown"));
            if (!connection.ConnectAsync().AsTask().Wait(ListenDeadline))
            {
                throw new TimeoutException("The system bus did not answer");
            }

            var match = connection.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Sender = "org.freedesktop.login1", Interface = "org.freedesktop.DBus.Properties", Member = "PropertiesChanged", PathNamespace = "/org/freedesktop/login1/session", Arg0 = "org.freedesktop.login1.Session" },
                static (Message m, object? _) => true,
                (Notification<bool> _) => changed.Set(),
                emitOnCapturedContext: false).AsTask();
            if (!match.Wait(ListenDeadline))
            {
                throw new TimeoutException("logind's change signal could not be subscribed to");
            }

            return connection;
        }
        catch (Exception e)
        {
            Log.Write($"Login session changes are polled every {Every.TotalMilliseconds} ms: {e.GetBaseException().Message}");
            connection?.Dispose();
            return null;
        }
    }

    /// <summary>True when this user's session is in front, false when it is not, null when logind did not say.</summary>
    private bool? Active()
    {
        var read = bus.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(
                    destination: "org.freedesktop.login1",
                    path: SessionPath,
                    @interface: "org.freedesktop.DBus.Properties",
                    member: "GetAll",
                    signature: "s");
                writer.WriteString("org.freedesktop.login1.Session");
                return writer.CreateMessage();
            },
            (Message reply, object? _) => reply.GetBodyReader().ReadDictionaryOfStringToVariantValue(),
            out var properties);

        // State, a string, rather than the Active boolean: "active" exactly when Active is true, and read the way
        // the rest of this app already reads properties. "online" is a session in the background, "closing" one
        // being logged out of.
        return read ? ActiveOf(properties) : null;
    }
}
