using Edgepad.Media;
using Tmds.DBus.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// Sleeping and shutting down this laptop through logind (<c>org.freedesktop.login1</c> on the system bus), for the
/// end-of-media action. Calls block up to the bus deadline, so they run on the media thread, never a session's reader.
/// </summary>
internal static class LinuxPower
{
    private const string Service = "org.freedesktop.login1";
    private const string ManagerPath = "/org/freedesktop/login1";
    private const string ManagerInterface = "org.freedesktop.login1.Manager";

    private static readonly Bus Logind = Bus.OnSystem();

    // Only a yes is kept: a bus that was not up yet is asked again the next time.
    private static volatile bool reachable;

    /// <summary>
    /// Asked, with the 60 s cancellable window, instead of powering off at once. The Shell sets it; it is given the
    /// action that powers off. With none (no UI), the laptop powers off straight away.
    /// </summary>
    internal static event Action<Action>? ShutdownRequested;

    /// <summary>Whether logind answers and allows suspending or powering off. Gates the <c>endaction</c> feature token.</summary>
    internal static bool Reachable
    {
        get
        {
            if (!reachable)
            {
                reachable = Allowed(Ask("CanSuspend")) || Allowed(Ask("CanPowerOff"));
            }

            return reachable;
        }
    }

    /// <summary>logind's <c>Can*</c> answer: "yes" and "challenge" (the user would be asked) are possible, "na" and "no" are not.</summary>
    internal static bool Allowed(string? answer) => answer is "yes" or "challenge";

    /// <summary>The logind method for an action.</summary>
    internal static string MethodFor(EndKind kind) => kind == EndKind.Sleep ? "Suspend" : "PowerOff";

    /// <summary>Sleeps now, or hands a shutdown to the countdown window. False when logind refused or is not there.</summary>
    internal static bool Perform(EndKind kind)
    {
        if (kind == EndKind.Shutdown && ShutdownRequested is { } countdown)
        {
            countdown(() => Call("PowerOff"));
            return true;
        }

        return Call(MethodFor(kind));
    }

    /// <summary>
    /// Synchronous, because a <see cref="MessageWriter"/> may not live across an await (CS4007). <paramref name="member"/>
    /// is Suspend or PowerOff, which take "interactive": false, so no password prompt appears on a laptop nobody is at.
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

    private static bool Call(string member) => Logind.Call(c => ActionMessage(c, member));

    private static string? Ask(string member) =>
        Logind.Call(c => QueryMessage(c, member), (Message reply, object? _) => reply.GetBodyReader().ReadString(), out var answer)
            ? answer
            : null;
}
