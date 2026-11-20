using System.Diagnostics;
using System.Runtime.InteropServices;
using Edgepad.Media;

namespace Edgepad.Dispatch;

/// <summary>What the phone's "when the media ends" does to this laptop: sleep now, or shut down after a cancellable countdown.</summary>
internal static partial class WindowsPower
{
    /// <summary>True when the action was started. Shutdown only starts the countdown; the owner can still cancel it.</summary>
    public static bool Perform(EndKind kind)
    {
        if (kind == EndKind.Sleep)
        {
            // hibernate false, force false, wake events enabled false. HW: Windows may hibernate instead if sleep is off.
            return SetSuspendState(false, false, false);
        }

        ShutdownCountdown.Show(ShutDownNow);
        return true;
    }

    /// <summary>ACTION 62, or the session ending: take the countdown down without shutting down.</summary>
    public static void CancelShutdown() => ShutdownCountdown.Dismiss();

    private static void ShutDownNow()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Write($"Shutdown at the end of the media failed: {ex.Message}");
        }
    }

    [LibraryImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);
}
