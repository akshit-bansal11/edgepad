using System.Collections.Concurrent;
using Tmds.DBus.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// One desktop notification (<c>org.freedesktop.Notifications.Notify</c>), for the moments a window or a tray menu may
/// not be seen: a question waiting, input that cannot work, an update. Sent from the thread pool, because a D-Bus call
/// may wait for its deadline; a desktop with no notification service loses it, and the log still has the line.
/// </summary>
internal static class Notifier
{
    private const string Service = "org.freedesktop.Notifications";
    private const string DefaultAction = "default";

    private static readonly Bus Session = Bus.OnSession();

    // Clicks to act on, by notification id, until the notification is clicked or closed.
    private static readonly ConcurrentDictionary<uint, Action> Clicks = new();
    private static int watching;

    public static void Send(string summary, string body) =>
        ThreadPool.QueueUserWorkItem(_ => Session.Call(c => Build(c, summary, body, false)));

    /// <summary>The same, waiting for the call: for a process about to exit, whose pool threads would die with it.</summary>
    public static void SendNow(string summary, string body) => Session.Call(c => Build(c, summary, body, false));

    /// <summary>
    /// A notification whose click (the default action) runs <paramref name="onClick"/> once, on a thread the bus
    /// connection owns. A desktop that shows no actions just never calls it.
    /// </summary>
    public static void SendClickable(string summary, string body, Action onClick) =>
        ThreadPool.QueueUserWorkItem(work =>
        {
            WatchClicks();
            if (Session.Call(c => Build(c, summary, body, true), (Message reply, object? _) => reply.GetBodyReader().ReadUInt32(), out var id))
            {
                Clicks[id] = onClick;
            }
        });

    // One connection for the process, opened the first time a clickable notification goes out; a failure is tried again next time.
    private static void WatchClicks()
    {
        if (Interlocked.CompareExchange(ref watching, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var bus = new DBusConnection(DBusAddress.Session ?? throw new IOException("The session bus address is unknown"));
                await bus.ConnectAsync();
                _ = await bus.AddMatchAsync(
                    Rule("ActionInvoked"),
                    static (Message m, object? _) =>
                    {
                        var reader = m.GetBodyReader();
                        return (Id: reader.ReadUInt32(), Key: reader.ReadString());
                    },
                    static (Exception? ex, (uint Id, string Key) invoked, object? _, object? _) =>
                    {
                        if (ex is null && invoked.Key == DefaultAction && Clicks.TryRemove(invoked.Id, out var click))
                        {
                            Run(click);
                        }
                    },
                    null,
                    null,
                    false,
                    ObserverFlags.None);
                _ = await bus.AddMatchAsync(
                    Rule("NotificationClosed"),
                    static (Message m, object? _) => m.GetBodyReader().ReadUInt32(),
                    static (Exception? ex, uint id, object? _, object? _) =>
                    {
                        if (ex is null)
                        {
                            _ = Clicks.TryRemove(id, out _);
                        }
                    },
                    null,
                    null,
                    false,
                    ObserverFlags.None);
            }
            catch (Exception e)
            {
                Log.Write($"Notification clicks cannot be heard: {e.Message}");
                Volatile.Write(ref watching, 0);
            }
        });
    }

    private static MatchRule Rule(string member) =>
        new() { Type = MessageType.Signal, Sender = Service, Interface = Service, Member = member };

    private static void Run(Action click)
    {
        try
        {
            click();
        }
        catch (Exception e)
        {
            Log.Write($"A notification click failed: {e.Message}");
        }
    }

    private static MessageBuffer Build(DBusConnection c, string summary, string body, bool clickable)
    {
        using var writer = c.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: Service,
            path: "/org/freedesktop/Notifications",
            @interface: Service,
            member: "Notify",
            signature: "susssasa{sv}i");
        writer.WriteString("Edgepad");
        writer.WriteUInt32(0);
        writer.WriteString("edgepad");
        writer.WriteString(summary);
        writer.WriteString(body);
        var actions = writer.WriteArrayStart(DBusType.String);
        if (clickable)
        {
            writer.WriteString(DefaultAction);
            writer.WriteString("Open");
        }

        writer.WriteArrayEnd(actions);
        var hints = writer.WriteDictionaryStart();
        writer.WriteDictionaryEnd(hints);
        writer.WriteInt32(-1);
        return writer.CreateMessage();
    }
}
