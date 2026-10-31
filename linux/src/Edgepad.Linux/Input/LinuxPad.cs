using System.Runtime.InteropServices;
using Edgepad.Protocol;

namespace Edgepad.Input;

/// <summary>
/// The laptop's virtual Xbox controller on Linux: a second uinput device, beside the keyboard, plugged in when the
/// phone sends PAD_ATTACH and unplugged on PAD_DETACH or when the session ends. The contract is the Windows
/// VirtualPad's: every entry point answers with a <see cref="PadStatus"/> token or a bool and never throws, because it
/// runs inside the dispatcher on the session thread, where an escape would take the link with it.
/// <para>
/// No driver to install here: /dev/uinput is the driver. no-driver means it cannot be opened (no udev rule for this
/// user, or the module not loaded); attach-failed means it opened and the kernel still would not create the pad.
/// </para>
/// <para>
/// Used from one session thread only, like VirtualPad: the dispatcher calls it and the session's own cleanup
/// disposes it, both on the read loop's thread.
/// </para>
/// </summary>
internal sealed class LinuxPad(string devicePath = Uinput.DevicePath) : IDisposable
{
    private Uinput? device;
    private bool? available;

    /// <summary>
    /// Whether a controller could be offered at all, sent after the handshake so the phone can hide the pad rather
    /// than offer a button that does nothing. Asked once per session, as on Windows: the phone presses attach as
    /// often as it likes, and a fixed udev rule is seen at the next connection.
    /// </summary>
    public string Probe()
    {
        if (available is null)
        {
            using var probe = Uinput.Open(devicePath, out var error);
            available = probe is not null;
            if (probe is null)
            {
                Log.Write($"Virtual controller: {error}. The gamepad is off; see docs/LINUX.md for the udev rule");
            }
        }

        return available.Value ? PadStatus.Ready : PadStatus.NoDriver;
    }

    /// <summary>Plugs the pad in, and says how that went as one <see cref="PadStatus"/> token.</summary>
    public string Attach()
    {
        if (device is not null)
        {
            // Asking twice is not an error: the phone sends PAD_ATTACH whenever it opens the gamepad screen, and
            // replugging under a running game would cost it its controller for nothing.
            return PadStatus.Ready;
        }

        if (Probe() != PadStatus.Ready || Uinput.Open(devicePath, out _) is not { } opened)
        {
            return PadStatus.NoDriver;
        }

        if (!UinputPad.Create(opened))
        {
            // Read before Dispose, whose own calls would overwrite it.
            var reason = Marshal.GetLastPInvokeErrorMessage();
            opened.Dispose();
            Log.Write($"Virtual controller: uinput would not create one: {reason}");
            return PadStatus.AttachFailed;
        }

        device = opened;
        Log.Write("Virtual controller plugged in");
        return PadStatus.Ready;
    }

    /// <summary>Unplugs the pad, and says whether another one could be plugged in after it.</summary>
    public string Detach()
    {
        Unplug();
        return Probe();
    }

    /// <summary>
    /// Writes one frame as one report. False when nothing is plugged in, which the dispatcher counts as a dropped
    /// frame, or when the kernel refused the write.
    /// </summary>
    public bool Update(PadState state)
    {
        if (device is null)
        {
            return false;
        }

        Span<InputEvent> events = stackalloc InputEvent[UinputPad.EventCount];
        UinputPad.Fill(state, events);
        if (device.Write(events))
        {
            return true;
        }

        // Logged once and given up on, as on Windows: a device that refused one report will refuse the next, and a
        // log line per frame would be file I/O on the input path. The phone can attach again.
        Log.Write($"Virtual controller: the pad stopped taking reports: {Marshal.GetLastPInvokeErrorMessage()}");
        Unplug();
        return false;
    }

    /// <summary>
    /// A phone that drops mid-game must not leave a controller plugged in with a stick pushed forward. Destroying
    /// the device is what clears it: the game sees the pad go away, not one frozen mid-input.
    /// </summary>
    public void Dispose() => Unplug();

    private void Unplug()
    {
        if (device is not { } plugged)
        {
            return;
        }

        device = null;
        plugged.Dispose();
        Log.Write("Virtual controller unplugged");
    }
}
