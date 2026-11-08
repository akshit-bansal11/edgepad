using Tmds.DBus.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// One desktop notification (<c>org.freedesktop.Notifications.Notify</c>), for the moments a window or a tray menu may
/// not be seen: a question waiting, input that cannot work, an update. Sent from the thread pool, because a D-Bus call
/// may wait for its deadline; a desktop with no notification service loses it, and the log still has the line.
/// </summary>
internal static class Notifier
{
    private static readonly Bus Session = Bus.OnSession();

    public static void Send(string summary, string body) =>
        ThreadPool.QueueUserWorkItem(_ => Session.Call(c => Build(c, summary, body)));

    /// <summary>The same, waiting for the call: for a process about to exit, whose pool threads would die with it.</summary>
    public static void SendNow(string summary, string body) => Session.Call(c => Build(c, summary, body));

    private static MessageBuffer Build(DBusConnection c, string summary, string body)
    {
        using var writer = c.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: "org.freedesktop.Notifications",
            path: "/org/freedesktop/Notifications",
            @interface: "org.freedesktop.Notifications",
            member: "Notify",
            signature: "susssasa{sv}i");
        writer.WriteString("Edgepad");
        writer.WriteUInt32(0);
        writer.WriteString("edgepad");
        writer.WriteString(summary);
        writer.WriteString(body);
        var actions = writer.WriteArrayStart(DBusType.String);
        writer.WriteArrayEnd(actions);
        var hints = writer.WriteDictionaryStart();
        writer.WriteDictionaryEnd(hints);
        writer.WriteInt32(-1);
        return writer.CreateMessage();
    }
}
