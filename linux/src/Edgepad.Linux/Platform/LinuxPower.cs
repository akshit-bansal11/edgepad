using Edgepad.Power;
using Edgepad.Ui;
using Tmds.DBus.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// Sleeping, hibernating, restarting and shutting down this laptop through logind (<c>org.freedesktop.login1</c> on the
/// system bus), for the phone's power control. Calls block up to the bus deadline, so they run off a session's reader.
/// A power feature is offered only when logind answers exactly "yes" to its <c>Can*</c> call.
/// </summary>
internal sealed class LinuxPower : IPowerPlatform
{
    private const string Service = "org.freedesktop.login1";
    private const string ManagerPath = "/org/freedesktop/login1";
    private const string ManagerInterface = "org.freedesktop.login1.Manager";

    private static readonly PowerCaps[] All = [PowerCaps.Sleep, PowerCaps.Hibernate, PowerCaps.Restart, PowerCaps.ShutDown];

    private static readonly Bus Logind = Bus.OnSystem();

    // Only a yes is kept: a bus that was not up yet is asked again the next time.
    private volatile PowerCaps known;

    // True once logind has answered every Can* query this session.
    private bool asked;

    /// <summary>The one instance of this process.</summary>
    internal static LinuxPower Shared { get; } = new();

    /// <summary>
    /// Asked, with the 60 s cancellable window, instead of restarting or powering off at once. The Shell sets it. With
    /// none (no UI), a restart or shut down is refused: it never happens without the countdown.
    /// </summary>
    internal static event Action<PowerKind, int, Action<bool>>? CountdownRequested;

    // Whichever session is current hears the countdown's state, not only the one that started it.
    private static Action<string>? listener;

    public PowerCaps Caps
    {
        get
        {
            // Asked once per session start (Listen clears the answer), so a "no" is not re-asked on every action; a bus
            // that did not answer is asked again.
            if (Volatile.Read(ref asked))
            {
                return known;
            }

            var answered = true;
            foreach (var cap in All)
            {
                var answer = Ask(CanFor(cap));
                answered &= answer is not null;
                if (Allowed(answer))
                {
                    known |= cap;
                }
            }

            Volatile.Write(ref asked, answered);
            return known;
        }
    }

    /// <summary>A session calls this on start: it becomes the one told the countdown state, and logind is asked afresh.</summary>
    internal static void Listen(Action<string> send)
    {
        Volatile.Write(ref listener, send);
        Shared.known = PowerCaps.None;
        Volatile.Write(ref Shared.asked, false);
    }

    /// <summary>Clears the listener only if it is still <c>send</c>, so a session ending after its successor started leaves the successor's.</summary>
    internal static void Unlisten(Action<string> send) => Interlocked.CompareExchange(ref listener, null, send);

    /// <summary>Tells the current session, if any, the countdown state.</summary>
    internal static void Report(string state) => Volatile.Read(ref listener)?.Invoke(state);

    public (PowerKind Kind, int SecondsLeft)? Pending => ShutdownCountdown.Pending;

    /// <summary>logind's <c>Can*</c> answer: only "yes" counts; "challenge" (the user would be asked), "na" and "no" do not.</summary>
    internal static bool Allowed(string? answer) => answer == "yes";

    /// <summary>The logind method for a power action.</summary>
    internal static string MethodFor(PowerCaps cap) =>
        cap switch
        {
            PowerCaps.Sleep => "Suspend",
            PowerCaps.Hibernate => "Hibernate",
            PowerCaps.Restart => "Reboot",
            _ => "PowerOff",
        };

    /// <summary>The logind query that says whether <see cref="MethodFor"/> is allowed.</summary>
    internal static string CanFor(PowerCaps cap) => "Can" + MethodFor(cap);

    public bool Sleep() => Call(MethodFor(PowerCaps.Sleep));

    public bool Hibernate() => Call(MethodFor(PowerCaps.Hibernate));

    public bool Execute(PowerKind kind) => Call(MethodFor(kind == PowerKind.Restart ? PowerCaps.Restart : PowerCaps.ShutDown));

    public bool StartCountdown(PowerKind kind, int seconds, Action<bool> ended)
    {
        if (CountdownRequested is not { } show)
        {
            Log.Write("Restart or shut down refused: no countdown window to show");
            return false;
        }

        // One window at a time; a countdown a dropped session started is still the one to cancel.
        if (Pending is not null)
        {
            return false;
        }

        show(kind, seconds, ended);
        return true;
    }

    public void CancelCountdown() => ShutdownCountdown.Cancel();

    /// <summary>
    /// Synchronous, because a <see cref="MessageWriter"/> may not live across an await (CS4007). <paramref name="member"/>
    /// takes "interactive": false, so no password prompt appears on a laptop nobody is at.
    /// </summary>
    internal static MessageBuffer ActionMessage(DBusConnection connection, string member)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: Service,
            path: ManagerPath,
            @interface: ManagerInterface,
            member: member,
            signature: "b");
        writer.WriteBool(false);
        return writer.CreateMessage();
    }

    private static MessageBuffer QueryMessage(DBusConnection connection, string member)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: ManagerPath, @interface: ManagerInterface, member: member);
        return writer.CreateMessage();
    }

    // HW: verify on device (logind Suspend, Hibernate, Reboot and PowerOff, and the four Can* answers).
    private static bool Call(string member) => Logind.Call(c => ActionMessage(c, member));

    private static string? Ask(string member) =>
        Logind.Call(c => QueryMessage(c, member), (Message reply, object? _) => reply.GetBodyReader().ReadString(), out var answer)
            ? answer
            : null;
}
