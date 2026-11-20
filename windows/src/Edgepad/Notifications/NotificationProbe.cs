using System.Runtime.InteropServices;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Edgepad.Notifications;

/// <summary>
/// The 4.9.0 spike, run as <c>Edgepad.exe --probe-notifications</c>: can an unpackaged exe read the owner's toasts
/// through <see cref="UserNotificationListener"/>? Writes the access status, the answer to the access request, any
/// exception with its HResult, and the number of toasts. Never a title, a text or an app name from a notification.
/// </summary>
internal static class NotificationProbe
{
    public static void Run()
    {
        Log.Write("Notification probe: start");

        // Off the UI thread: the access request can wait on a window that this thread would otherwise block.
        Task.Run(ProbeAsync).GetAwaiter().GetResult();

        Log.Write("Notification probe: done");
    }

    private static async Task ProbeAsync()
    {
        try
        {
            var listener = UserNotificationListener.Current;
            Log.Write($"Notification probe: access status {listener.GetAccessStatus()}");

            var access = await listener.RequestAccessAsync();
            Log.Write($"Notification probe: access request {access}");

            var toasts = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            Log.Write($"Notification probe: {toasts.Count} toast notifications");
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Log.Write($"Notification probe: {e.GetType().Name} 0x{e.HResult:X8}: {e.Message}");
        }
    }
}
