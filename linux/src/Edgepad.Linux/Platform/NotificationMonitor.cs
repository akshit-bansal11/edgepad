using System.Globalization;
using Avalonia.Threading;
using Edgepad.Notify;
using Edgepad.Ui;
using Tmds.DBus.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// Forwards the desktop's notifications to one connected phone. A dedicated session-bus connection becomes a monitor
/// (<c>org.freedesktop.DBus.Monitoring.BecomeMonitor</c>) for the single rule below, and each <c>Notify</c> call it
/// sees is encoded, limited and handed to <c>send</c>. Event-driven, no polling. It runs only while the tray switch
/// is on and the phone has sent SET NOTIFY 1; otherwise no connection is open. A monitor never replies, so
/// <see cref="HandleMethodAsync"/> never does. Content is never logged. Whether the running bus (dbus-daemon,
/// dbus-broker) allows a same-user monitor is UNKNOWN: a refusal is logged once and drops the feature token.
/// </summary>
internal sealed class NotificationMonitor : IPathMethodHandler, IDisposable
{
    internal const string Rule = "type='method_call',interface='org.freedesktop.Notifications',member='Notify'";

    private const string ObjectPath = "/org/freedesktop/Notifications";
    private const string NotifySignature = "susssasa{sv}i";
    private const string OwnApp = "Edgepad";

    private static int refused;

    private readonly Action<string, byte[]> send;
    private readonly Func<bool> setting;
    private readonly NotifyLimiter limiter;
    private readonly Lock gate = new();
    private CancellationTokenSource? running;
    private volatile bool disposed;
    private int serial;

    /// <param name="send">Sends one BLOB 5: the label, then the bytes.</param>
    public NotificationMonitor(Action<string, byte[]> send, Func<bool>? setting = null, Func<long>? clockMs = null)
    {
        this.send = send;
        this.setting = setting ?? (() => NotifySetting.Enabled);
        limiter = new NotifyLimiter(clockMs ?? (() => Environment.TickCount64));
        NotifySetting.Changed += Update;
    }

    /// <summary>True once the bus refused the monitor. The notify feature is then not offered to phones.</summary>
    public static bool Refused => Volatile.Read(ref refused) != 0;

    public string Path => ObjectPath;

    public bool HandlesChildPaths => false;

    /// <summary>Whether the connected phone sent SET NOTIFY 1. Forgotten when the session ends with this object.</summary>
    internal volatile bool PhoneOn;

    /// <summary>The phone's SET NOTIFY.</summary>
    public void SetPhone(bool on)
    {
        PhoneOn = on;
        Update();
    }

    public void Dispose()
    {
        NotifySetting.Changed -= Update;
        disposed = true;
        PhoneOn = false;
        Update();
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        // Top-level boundary of a D-Bus dispatch. No reply, ever: a monitor must not send. The log never carries text.
        try
        {
            var request = context.Request;
            if (request.MemberAsString == "Notify" && request.SignatureAsString == NotifySignature)
            {
                var reader = request.GetBodyReader();
                var app = reader.ReadString();
                _ = reader.ReadUInt32();
                _ = reader.ReadString();
                var title = reader.ReadString();
                var body = reader.ReadString();
                Deliver(app, title, body);
            }
        }
        catch (Exception e)
        {
            Log.Write($"A notification could not be forwarded: {e.GetType().Name}");
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Sends one notification if switch and phone both allow it and it passes the encoder and the limiter.</summary>
    internal void Deliver(string app, string title, string body)
    {
        if (disposed || !PhoneOn || !setting() || app == OwnApp)
        {
            return;
        }

        if (NotifyMessage.Encode(app, title, body) is not { } data)
        {
            return;
        }

        bool accepted;
        lock (limiter)
        {
            accepted = limiter.TryAccept(app, title, body);
        }

        if (accepted)
        {
            send(Interlocked.Increment(ref serial).ToString(CultureInfo.InvariantCulture), data);
        }
    }

    private static MessageBuffer BecomeMonitorMessage(DBusConnection bus)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: "org.freedesktop.DBus",
            path: "/org/freedesktop/DBus",
            @interface: "org.freedesktop.DBus.Monitoring",
            member: "BecomeMonitor",
            signature: "asu");
        var rules = writer.WriteArrayStart(DBusType.String);
        writer.WriteString(Rule);
        writer.WriteArrayEnd(rules);
        writer.WriteUInt32(0);
        return writer.CreateMessage();
    }

    private void Update()
    {
        lock (gate)
        {
            var want = !disposed && PhoneOn && setting() && !Refused;
            if (want && running is null)
            {
                var start = new CancellationTokenSource();
                running = start;
                _ = Task.Run(() => RunAsync(start.Token));
            }
            else if (!want && running is { } stop)
            {
                running = null;
                stop.Cancel();
            }
        }
    }

    private async Task RunAsync(CancellationToken stop)
    {
        DBusConnection? bus = null;
        var monitoring = false;
        try
        {
            bus = new DBusConnection(DBusAddress.Session ?? throw new IOException("The session bus address is unknown"));
            await bus.ConnectAsync();
            bus.AddMethodHandler(this);
            await bus.CallMethodAsync(BecomeMonitorMessage(bus));
            monitoring = true;
            Log.Write("Notifications are forwarded to the phone");
            var lost = await bus.DisconnectedAsync().WaitAsync(stop);
            Log.Write($"Notifications are no longer forwarded: the session bus went away{(lost is null ? "" : $" ({lost.Message})")}");
        }
        catch (OperationCanceledException)
        {
            // Switched off, or the phone or the session ended.
        }
        catch (Exception e)
        {
            if (!monitoring && Interlocked.Exchange(ref refused, 1) == 0)
            {
                Log.Write($"The session bus did not allow a notification monitor, so notifications are not forwarded: {e.Message}");
                Dispatcher.UIThread.Post(Annotator.FeaturesChanged);
            }
            else
            {
                Log.Write($"Notification forwarding stopped: {e.Message}");
            }
        }
        finally
        {
            bus?.Dispose();
            lock (gate)
            {
                // A run that ended on its own leaves the next switch-on free to start a new one.
                if (running is { } own && own.Token == stop)
                {
                    running = null;
                }
            }
        }
    }
}
