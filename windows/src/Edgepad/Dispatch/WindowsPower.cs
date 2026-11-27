using System.Diagnostics;
using System.Runtime.InteropServices;
using Edgepad.Power;

namespace Edgepad.Dispatch;

/// <summary>
/// Sleep, hibernate, restart and shut down on the phone's request. What the laptop can do is read once, from
/// <c>GetPwrCapabilities</c>; restart and shut down go through <see cref="ShutdownCountdown"/>, whose window is one
/// for the whole app, so a countdown outlives the session that started it.
/// </summary>
internal sealed partial class WindowsPower : IPowerPlatform
{
    private const uint WmSysCommand = 0x0112;
    private const nint HwndBroadcast = 0xFFFF;
    private const nint ScMonitorPower = 0xF170;
    private const nint MonitorOff = 2;

    private static Action<string>? listener;

    private readonly bool suspend;

    public WindowsPower()
    {
        // HW: verify on device
        if (GetPwrCapabilities(out var c))
        {
            suspend = c.SystemS1 != 0 || c.SystemS2 != 0 || c.SystemS3 != 0;
            Caps = CapsFrom(c.SystemS1 != 0, c.SystemS2 != 0, c.SystemS3 != 0, c.SystemS4 != 0, c.HiberFilePresent != 0, c.AoAc != 0);
        }
        else
        {
            Caps = PowerCaps.Restart | PowerCaps.ShutDown;
        }
    }

    public PowerCaps Caps { get; }

    /// <summary>
    /// The countdown outlives the session that started it, so its state goes to whichever session is current, not to the one
    /// whose <see cref="PowerControl"/> started it. A session calls this on start and <see cref="Unlisten"/> on end.
    /// </summary>
    public static void Listen(Action<string> send) => Volatile.Write(ref listener, send);

    /// <summary>Clears the listener only if it is still <c>send</c>, so a session ending after its successor started leaves the successor's.</summary>
    public static void Unlisten(Action<string> send) => Interlocked.CompareExchange(ref listener, null, send);

    /// <summary>Tells the current session, if any, the countdown state.</summary>
    public static void Report(string state) => Volatile.Read(ref listener)?.Invoke(state);

    public (PowerKind Kind, int SecondsLeft)? Pending => ShutdownCountdown.Pending;

    /// <summary>
    /// Sleep when the laptop has S1 to S3, or Modern Standby (AoAc, S0 low power idle); hibernate only with S4 and a
    /// hibernation file; restart and shut down always. HW: verify on device.
    /// </summary>
    public static PowerCaps CapsFrom(bool s1, bool s2, bool s3, bool s4, bool hiberFile, bool aoac)
    {
        var caps = PowerCaps.Restart | PowerCaps.ShutDown;
        if (s1 || s2 || s3 || aoac)
        {
            caps |= PowerCaps.Sleep;
        }

        if (s4 && hiberFile)
        {
            caps |= PowerCaps.Hibernate;
        }

        return caps;
    }

    public bool Sleep()
    {
        if (suspend)
        {
            // hibernate false, force false, wake events enabled. HW: verify on device
            return SetSuspendState(false, false, false);
        }

        // Modern Standby only: SetSuspendState has no S3 to enter, and turning the display off is what lets Windows
        // drop into S0 low power idle. Posted, not sent, so a hung window cannot hold the caller. HW: verify on device
        return PostMessage(HwndBroadcast, WmSysCommand, ScMonitorPower, MonitorOff);
    }

    // HW: verify on device
    public bool Hibernate() => SetSuspendState(true, false, false);

    public bool StartCountdown(PowerKind kind, int seconds, Action<bool> ended) => ShutdownCountdown.Show(kind, seconds, ended);

    public void CancelCountdown() => ShutdownCountdown.Dismiss();

    public bool Execute(PowerKind kind)
    {
        try
        {
            // HW: verify on device
            using var _ = Process.Start(new ProcessStartInfo("shutdown.exe", kind == PowerKind.Restart ? "/r /t 0" : "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Write($"Restart or shut down from the phone failed: {ex.Message}");
            return false;
        }
    }

    [LibraryImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool GetPwrCapabilities(out SystemPowerCapabilities capabilities);

    [LibraryImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>
    /// SYSTEM_POWER_CAPABILITIES (winnt.h), 76 bytes: fifteen BOOLEANs from PowerButtonPresent (offset 0), then
    /// ProcessorMinThrottle, ProcessorMaxThrottle, FastSystemS4, Hiberboot, WakeAlarmPresent, AoAc (20). Only the
    /// fields read here are named; the rest of the 76 bytes is left for Windows to fill.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 76)]
    private struct SystemPowerCapabilities
    {
        [FieldOffset(3)]
        public byte SystemS1;

        [FieldOffset(4)]
        public byte SystemS2;

        [FieldOffset(5)]
        public byte SystemS3;

        [FieldOffset(6)]
        public byte SystemS4;

        [FieldOffset(8)]
        public byte HiberFilePresent;

        [FieldOffset(20)]
        public byte AoAc;
    }
}
